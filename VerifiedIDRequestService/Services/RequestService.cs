using Azure.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using static Microsoft.Entra.VerifiedID.IRequestService;

namespace Microsoft.Entra.VerifiedID; 
public class RequestService : IRequestService{
    protected IMemoryCache _cache;
    protected readonly ILogger<RequestService> _log;
    private IHttpClientFactory _httpClientFactory;
    private HttpClient _httpClient;
    private string _apiKey;
    private IConfiguration _configuration;
    private int _cacheTtl = 0;
    private HttpRequest _httpRequest;

    public RequestService( IConfiguration configuration, IMemoryCache memoryCache, ILogger<RequestService> log, IHttpClientFactory httpClientFactory ) {
        _configuration = configuration;
        _cache = memoryCache;
        _log = log;
        _httpClientFactory = httpClientFactory;
        _apiKey = System.Environment.GetEnvironmentVariable( "API-KEY" );
        _cacheTtl = _configuration.GetValue<int>( "AppSettings:CacheExpiresInSeconds", 300 );
    }
    private string GetRequestHostName() {
        string scheme = "https";// : this.Request.Scheme;
        string originalHost = _httpRequest.Headers["x-original-host"];
        string hostname = "";
        if (!string.IsNullOrEmpty( originalHost ))
            hostname = string.Format( "{0}://{1}", scheme, originalHost );
        else hostname = string.Format( "{0}://{1}", scheme, _httpRequest.Host );
        return hostname;
    }

    private bool IsMobile() {
        string userAgent = _httpRequest.Headers["User-Agent"];
        return (userAgent.Contains( "Android" ) || userAgent.Contains( "iPhone" ));
    }

    private string GetRequestParameter( string paramName, string configName ) {
        string paramValue = _httpRequest.HttpContext.Session.GetString( paramName );
        if (_httpRequest.Query.ContainsKey( paramName )) {
            paramValue = _httpRequest.Query[paramName].ToString();
        }
        if (string.IsNullOrWhiteSpace( paramValue ) && !string.IsNullOrWhiteSpace( configName )) {
            paramValue = _configuration[configName];
        }
        return paramValue;
    }

    private async Task<(bool ok, string? errmsg)> CreateHttpClient() {
        if (null == _httpClient) {
            var accessToken = await MsalAccessTokenHandler.GetAccessToken( _configuration );
            if (accessToken.token == String.Empty) {
                return (false, $"failed to acquire accesstoken: {accessToken.error} : {accessToken.error_description}");
            }
            _httpClient = _httpClientFactory.CreateClient();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", accessToken.token );
            // target an env
            string xMsDidTarget = GetRequestParameter( "x-ms-did-target", null );
            if (!string.IsNullOrWhiteSpace( xMsDidTarget )) {
                _httpClient.DefaultRequestHeaders.Add( "x-ms-did-target", xMsDidTarget );
            }
        }
        return (true, null);
    }
    private async Task<(HttpResponseMessage res, string response)> HttpPost( string url, string body ) {
        _log.LogTrace( $"Calling API {url}\nBody: {body}" );
        HttpResponseMessage res = await _httpClient.PostAsync( url, new StringContent( body, Encoding.UTF8, "application/json" ) );
        string response = await res.Content.ReadAsStringAsync();
        _log.LogTrace( $"Response from API {url}: StatusCode {res.StatusCode.ToString()}\nBody: {response}" );
        return (res, response);
    }

    private RequestError CreateRequestError( string code, string message, string target = null ) {
        RequestError re = new RequestError() {
            date = DateTime.UtcNow.ToString(),
            error = new OuterError() {
                code = code, 
                message = message,
                innererror = new InnerError() {
                    code = code,
                    message = message,
                    target = target
                }
            }
        };
        return re;
    }
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Common Handlers
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public CallbackEvent GetCachedRequestEvent( string requestId ) {
        if (!string.IsNullOrEmpty( requestId ) && _cache.TryGetValue( requestId, out string buf )) {
            JObject cachedData = JObject.Parse( buf );
            if ( cachedData.ContainsKey("callback") ) {
                CallbackEvent callback = JsonConvert.DeserializeObject<CallbackEvent>( cachedData["callback"].ToString() );
                return callback;
            }
        } 
        return null;
    }
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Issuance Request Handlers
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public async Task<(bool success, RequestResponse? response, RequestError? error)> PostIssuanceRequest( IssuanceRequest request ) {
        var result = await CreateHttpClient();
        if (!result.ok) {
            _log.LogError( result.errmsg );
            RequestError rerr = CreateRequestError( "Authentication", result.errmsg, "" );
            return (false, null, rerr);
        }
        string json = JsonConvert.SerializeObject( request, Newtonsoft.Json.Formatting.None, new JsonSerializerSettings {
            NullValueHandling = NullValueHandling.Ignore
        } );
        var post = await HttpPost( $"{_configuration["VerifiedID:ApiEndpoint"]}createIssuanceRequest", json );
        if (post.res.StatusCode == HttpStatusCode.Created) {
            RequestResponse resp = JsonConvert.DeserializeObject<RequestResponse>(post.response);
            resp.id = request.callback.state;
            if (request.pin != null) {
                resp.pin = request.pin.value; // pass it back to the UI
            }
            //We use in memory cache to keep state about the request. The UI will check the state when calling the presentationResponse method
            var cacheData = new {
                status = "request_created",
                message = "Waiting for QR code to be scanned",
                expiry = resp.expiry.ToString(),
            };
            json = JsonConvert.SerializeObject( cacheData, Newtonsoft.Json.Formatting.None, new JsonSerializerSettings {
                NullValueHandling = NullValueHandling.Ignore
            } );
            _cache.Set( request.callback.state, json, DateTimeOffset.Now.AddSeconds( _cacheTtl ) );
            return (true, resp, null);
        } else {
            RequestError rerr = JsonConvert.DeserializeObject<RequestError>( post.response );
            _log.LogError( "Unsuccesfully called Request Service API" + post.response );
            return (false, null, rerr);
        }
    }
    public IssuanceRequest CreateIssuanceRequest( HttpRequest httpRequest, string stateId = null ) {
        _httpRequest = httpRequest;
        IssuanceRequest request = new IssuanceRequest() {
            includeQRCode = _configuration.GetValue( "VerifiedID:includeQRCode", false ),
            authority = _configuration["VerifiedID:DidAuthority"],
            registration = new Registration() {
                clientName = _configuration["VerifiedID:client_name"],
                purpose = _configuration.GetValue( "VerifiedID:purpose", "" )
            },
            callback = new Callback() {
                url = $"{GetRequestHostName()}/api/issuer/issuecallback",
                state = string.IsNullOrEmpty( stateId ) ? Guid.NewGuid().ToString() : stateId,
                headers = new Dictionary<string, string>() { { "api-key", this._apiKey } }
            },
            type = "ignore-this",
            manifest = _configuration["VerifiedID:CredentialManifest"],
            pin = null
        };
        if ("" == request.registration.purpose) {
            request.registration.purpose = null;
        }
        int issuancePinCodeLength = _configuration.GetValue( "VerifiedID:IssuancePinCodeLength", 0 );
        // if pincode is required, set it up in the request
        if (issuancePinCodeLength > 0 && !IsMobile()) {
            int pinCode = RandomNumberGenerator.GetInt32( 1, int.Parse( "".PadRight( issuancePinCodeLength, '9' ) ) );
            SetPinCode( request, string.Format( "{0:D" + issuancePinCodeLength.ToString() + "}", pinCode ) );
        }
        SetExpirationDate( request );
        return request;
    }

    public IssuanceRequest SetExpirationDate( IssuanceRequest request ) {
        string credentialExpiration = _configuration.GetValue( "VerifiedID:CredentialExpiration", "" );
        DateTime expDateUtc;
        DateTime utcNow = DateTime.UtcNow;
        // This is just examples for how to specify your own expiry dates
        switch (credentialExpiration.ToUpperInvariant()) {
            case "EOD":
                expDateUtc = DateTime.UtcNow;
                break;
            case "EOW":
                int start = (int)utcNow.DayOfWeek;
                int target = (int)DayOfWeek.Sunday;
                if (target <= start)
                    target += 7;
                expDateUtc = utcNow.AddDays( target - start );
                break;
            case "EOM":
                expDateUtc = new DateTime( utcNow.Year, utcNow.Month, DateTime.DaysInMonth( utcNow.Year, utcNow.Month ) );
                break;
            case "EOQ":
                int quarterEndMonth = (int)(3 * Math.Ceiling( (double)utcNow.Month / 3 ));
                expDateUtc = new DateTime( utcNow.Year, quarterEndMonth, DateTime.DaysInMonth( utcNow.Year, quarterEndMonth ) );
                break;
            case "EOY":
                expDateUtc = new DateTime( utcNow.Year, 12, 31 );
                break;
            default:
                return request;
        }
        // Remember that this date is expressed in UTC and Wallets/Authenticator displays the expiry date 
        // in local timezone. So for example, EOY will be displayed as "Jan 1" if the user is in a timezone
        // east of GMT. Also, if you issue a VC that should expire 5pm locally, then you need to calculate
        // what 5pm locally is in UTC time
        request.expirationDate = $"{Convert.ToDateTime( expDateUtc ).ToString( "yyyy-MM-dd" )}T23:59:59.000Z";
        return request;
    }
    public IssuanceRequest SetPinCode( IssuanceRequest request, string pinCode = null ) {
        _log.LogTrace( "pin={0}", pinCode );
        if (string.IsNullOrWhiteSpace( pinCode )) {
            request.pin = null;
        } else {
            request.pin = new Pin() {
                length = pinCode.Length,
                value = pinCode
            };
        }
        return request;
    }

    public bool ValidateTenant( out string errmsg ) {
        errmsg = null;
        string manifestUrl = _configuration["VerifiedID:CredentialManifest"];
        if (string.IsNullOrWhiteSpace( manifestUrl )) {
            errmsg = $"Manifest missing in config file";
            return false;
        }
        string tenantId = _configuration["VerifiedID:TenantId"];
        string manifestTenantId = manifestUrl.Split( "/" )[5];
        if (manifestTenantId != tenantId) {
            errmsg = $"TenantId in ManifestURL {manifestTenantId}. does not match tenantId in config file {tenantId}";
            return false;
        }
        return true;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Presentation Request Handlers
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public async Task<(bool success, RequestResponse? response, RequestError? error)> PostPresentationRequest( PresentationRequest request ) {
        var result = await CreateHttpClient();
        if (!result.ok) {
            _log.LogError( result.errmsg );
            RequestError rerr = CreateRequestError( "Authentication", result.errmsg, "" );
            return (false, null, rerr);
        }
        string json = JsonConvert.SerializeObject( request, Newtonsoft.Json.Formatting.None, new JsonSerializerSettings {
            NullValueHandling = NullValueHandling.Ignore
        } );
        _log.LogTrace( $"Request API payload: {json}" );
        var post = await HttpPost( $"{_configuration["VerifiedID:ApiEndpoint"]}createPresentationRequest", json );
        if (post.res.StatusCode == HttpStatusCode.Created) {
            RequestResponse resp = JsonConvert.DeserializeObject<RequestResponse>( post.response );
            resp.id = request.callback.state;
            //We use in memory cache to keep state about the request. The UI will check the state when calling the presentationResponse method
            var cacheData = new {
                status = "request_created",
                message = "Waiting for QR code to be scanned",
                expiry = resp.expiry.ToString(),
            };
            json = JsonConvert.SerializeObject( cacheData, Newtonsoft.Json.Formatting.None, new JsonSerializerSettings {
                NullValueHandling = NullValueHandling.Ignore
            } );
            _cache.Set( request.callback.state, json, DateTimeOffset.Now.AddSeconds( _cacheTtl ) );
            return (true, resp, null);
        } else {
            _log.LogError( "Unsuccesfully called Request Service API" + post.response );
            RequestError rerr = JsonConvert.DeserializeObject<RequestError>( post.response );
            return (false, null, rerr);
        }
    }

    public PresentationRequest CreatePresentationRequest( HttpRequest httpRequest, string stateId = null, string credentialType = null, List<string> acceptedIssuers = null, string faceCheckPhotoClaim = null ) {
        _httpRequest = httpRequest;
        PresentationRequest request = new PresentationRequest() {
            includeQRCode = _configuration.GetValue( "VerifiedID:includeQRCode", false ),
            authority = _configuration["VerifiedID:DidAuthority"],
            registration = new Registration() {
                clientName = _configuration.GetValue( "VerifiedID:client_name", "Woodgrove Onboarding" ),
                purpose = _configuration.GetValue( "VerifiedID:purpose", "To prove your identity" )
            },
            callback = new Callback() {
                url = $"{GetRequestHostName()}/api/verifier/presentationcallback",
                state = (string.IsNullOrWhiteSpace( stateId ) ? Guid.NewGuid().ToString() : stateId),
                headers = new Dictionary<string, string>() { { "api-key", this._apiKey } }
            },
            includeReceipt = _configuration.GetValue( "VerifiedID:includeReceipt", false ),
            requestedCredentials = new List<RequestedCredential>(),
        };
        if ("" == request.registration.purpose) {
            request.registration.purpose = null;
        }
        if (string.IsNullOrEmpty( credentialType )) {
            credentialType = _configuration.GetValue( "VerifiedID:CredentialType", "VerifiedIdentity" );
        }
        bool allowRevoked = _configuration.GetValue( "VerifiedID:allowRevoked", false );
        bool validateLinkedDomain = _configuration.GetValue( "VerifiedID:validateLinkedDomain", true );
        AddRequestedCredential( request, credentialType, acceptedIssuers, allowRevoked, validateLinkedDomain );
        if (!string.IsNullOrWhiteSpace( faceCheckPhotoClaim )) {
            AddFaceCheck( request.requestedCredentials[0], faceCheckPhotoClaim );
        }
        return request;
    }
    public PresentationRequest AddRequestedCredential( PresentationRequest request
                                            , string credentialType, List<string> acceptedIssuers
                                            , bool allowRevoked = false, bool validateLinkedDomain = true ) {
        request.requestedCredentials.Add( new RequestedCredential() {
            type = credentialType,
            acceptedIssuers = (null == acceptedIssuers ? new List<string>() : acceptedIssuers),
            configuration = new Configuration() {
                validation = new Validation() {
                    allowRevoked = allowRevoked,
                    validateLinkedDomain = validateLinkedDomain
                }
            }
        } );
        return request;
    }
    public void AddFaceCheck( RequestedCredential requestedCredential, string photoClaimName = null ) {
        if (string.IsNullOrWhiteSpace( photoClaimName )) {
            photoClaimName = _configuration.GetValue( "VerifiedID:sourcePhotoClaimName", "photo" );
        }
        requestedCredential.configuration.validation.faceCheck = new FaceCheck() {
            sourcePhotoClaimName = photoClaimName,
            matchConfidenceThreshold = _configuration.GetValue( "VerifiedID:matchConfidenceThreshold", 70 )
        };
    }
    public bool IsFaceCheckRequested( PresentationRequest request ) {
        foreach (var rc in request.requestedCredentials) {
            if (rc.configuration.validation.faceCheck != null) {
                return true;
            }
        }
        return false;
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Event Callback handlers
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public async Task<(HttpStatusCode statusCode, CallbackEvent? callback, string? errorMessage)> HandleSelfiePostback( HttpRequest httpRequest, string id ) {
        string body = new System.IO.StreamReader( httpRequest.Body ).ReadToEnd();
        _log.LogTrace( body );
        // selfie request posts the JPEG image in the body (it comes from the user's mobile device)
        // while the others have a Verified ID JSON callback event structure
        string dataImage = "data:image/jpeg;base64,";
        int idx = body.IndexOf( ";base64," );
        if (-1 == idx) {
            return (HttpStatusCode.BadRequest, null, $"Image must be {dataImage}");
        }
        string photo = body.Substring( idx + 8 );
        CallbackEvent callback = new CallbackEvent() {
            requestId = id,
            state = id,
            requestStatus = "selfie_taken",
            photo = photo
        };
        return (HttpStatusCode.OK, callback, null);
    }
    public async Task<(HttpStatusCode statusCode, string? errorMessage)> HandleRequestCallback( HttpRequest httpRequest, IRequestService.RequestType requestType, string body ) {
        try {
            httpRequest.Headers.TryGetValue( "api-key", out var apiKey );
            if (requestType != IRequestService.RequestType.Selfie && this._apiKey != apiKey) {
                _log.LogTrace( "api-key wrong or missing" );
                return (HttpStatusCode.Unauthorized, "api-key wrong or missing" );
            }

            if (body == null) {
                body = await new System.IO.StreamReader( httpRequest.Body ).ReadToEndAsync();
                _log.LogTrace( body );
            }

            bool rc = false;
            string errorMessage = null;
            List<string> presentationStatus = new List<string>() { "request_retrieved", "presentation_verified", "presentation_error" };
            List<string> issuanceStatus = new List<string>() { "request_retrieved", "issuance_successful", "issuance_error" };
            List<string> selfieStatus = new List<string>() { "selfie_taken" };

            CallbackEvent callback = JsonConvert.DeserializeObject<CallbackEvent>( body );

            if ((requestType == IRequestService.RequestType.Presentation && presentationStatus.Contains( callback.requestStatus ))
                || (requestType == IRequestService.RequestType.Issuance && issuanceStatus.Contains( callback.requestStatus ))
                || (requestType == IRequestService.RequestType.Selfie && selfieStatus.Contains( callback.requestStatus ))) {
                if (!_cache.TryGetValue( callback.state, out string requestState )) {
                    errorMessage = $"Invalid state '{callback.state}'";
                } else {
                    JObject reqState = JObject.Parse( requestState );
                    reqState["status"] = callback.requestStatus;
                    if (reqState.ContainsKey( "callback" )) {
                        reqState["callback"] = body;
                    } else {
                        reqState.Add( "callback", body );
                    }
                    _cache.Set( callback.state, JsonConvert.SerializeObject( reqState ), DateTimeOffset.Now.AddSeconds( _cacheTtl ) );
                    rc = true;
                }
            } else {
                errorMessage = $"Unknown request status '{callback.requestStatus}'";
            }
            if (!rc) {
                return (HttpStatusCode.BadRequest, errorMessage );
            }
            return (HttpStatusCode.OK, null);
        } catch (Exception ex) {
            return (HttpStatusCode.BadRequest, ex.Message);
        }
    }

    public async Task<(bool success, JObject? result)> PollRequestStatus( HttpRequest httpRequest ) {
        JObject result = null;
        string state = httpRequest.Query["id"];
        if (string.IsNullOrEmpty( state )) {
            result = JObject.FromObject( new { status = "error", message = "Missing argument 'id'" } );
            return (false, result);
        }
        bool rc = true;
        if (_cache.TryGetValue( state, out string requestState )) {
            JObject reqState = JObject.Parse( requestState );
            string requestStatus = reqState["status"].ToString();
            CallbackEvent callback = null;
            switch (requestStatus) {
                case "request_created":
                    result = JObject.FromObject( new { status = requestStatus, message = "Waiting to scan QR code" } );
                    break;
                case "request_retrieved":
                    result = JObject.FromObject( new { status = requestStatus, message = "QR code is scanned. Waiting for user action..." } );
                    break;
                case "issuance_error":
                    callback = JsonConvert.DeserializeObject<CallbackEvent>( reqState["callback"].ToString() );
                    result = JObject.FromObject( new { status = requestStatus, message = "Issuance failed: " + callback.error.message } );
                    break;
                case "issuance_successful":
                    result = JObject.FromObject( new { status = requestStatus, message = "Issuance successful" } );
                    break;
                case "presentation_error":
                    callback = JsonConvert.DeserializeObject<CallbackEvent>( reqState["callback"].ToString() );
                    result = JObject.FromObject( new { status = requestStatus, message = "Presentation failed:" + callback.error.message } );
                    break;
                case "presentation_verified":
                    callback = JsonConvert.DeserializeObject<CallbackEvent>( reqState["callback"].ToString() );
                    JObject resp = JObject.Parse( JsonConvert.SerializeObject( new {
                        status = requestStatus,
                        message = "Presentation verified",
                        type = callback.verifiedCredentialsData[0].type,
                        claims = callback.verifiedCredentialsData[0].claims,
                        subject = callback.subject,
                        payload = callback.verifiedCredentialsData,
                    }, Newtonsoft.Json.Formatting.None, new JsonSerializerSettings {
                        NullValueHandling = NullValueHandling.Ignore
                    } ) );
                    if (null != callback.receipt && null != callback.receipt.vp_token) {
                        JObject vpToken = GetJsonFromJwtToken( callback.receipt.vp_token[0] );
                        JObject vc = GetJsonFromJwtToken( vpToken["vp"]["verifiableCredential"][0].ToString() );
                        resp.Add( new JProperty( "jti", vc["jti"].ToString() ) );
                    }
                    if (!string.IsNullOrWhiteSpace( callback.verifiedCredentialsData[0].expirationDate )) {
                        resp.Add( new JProperty( "expirationDate", callback.verifiedCredentialsData[0].expirationDate ) );
                    }
                    if (!string.IsNullOrWhiteSpace( callback.verifiedCredentialsData[0].issuanceDate )) {
                        resp.Add( new JProperty( "issuanceDate ", callback.verifiedCredentialsData[0].issuanceDate ) );
                    }
                    result = resp;
                    break;
                case "selfie_taken":
                    callback = JsonConvert.DeserializeObject<CallbackEvent>( reqState["callback"].ToString() );
                    result = JObject.FromObject( new { status = requestStatus, message = "Selfie taken", photo = callback.photo } );
                    break;
                default:
                    result = JObject.FromObject( new { status = "error", message = $"Invalid requestStatus '{requestStatus}'" } );
                    rc = false;
                    break;
            }
        } else {
            result = JObject.FromObject( new { status = "request_not_created", message = "No data" } );
            rc = false;
        }
        return (rc, result);
    }
    private JObject GetJsonFromJwtToken( string jwtToken ) {
        jwtToken = jwtToken.Replace( "_", "/" ).Replace( "-", "+" ).Split( "." )[1];
        jwtToken = jwtToken.PadRight( 4 * ((jwtToken.Length + 3) / 4), '=' );
        return JObject.Parse( System.Text.Encoding.UTF8.GetString( Convert.FromBase64String( jwtToken ) ) );
    }


    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // Selfie Request Handlers
    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public async Task<(bool success, SelfieRequest? request, string? error)> CreateSelfieRequest( HttpRequest httpRequest ) {
        _httpRequest = httpRequest;
        string hostname = GetRequestHostName();
        if (hostname.Contains( "://localhost:" ) || hostname.Contains( "://127.0.0.1:" )) {
            return (false, null, "localhost cannot be used" );
        }
        string id = id = Guid.NewGuid().ToString();
        SelfieRequest request = new SelfieRequest() {
            id = id,
            url = $"{hostname}/selfie?callbackUrl={hostname}/api/issuer/selfie/{id}",
            expiry = DateTimeOffset.UtcNow.AddMinutes( 5 ).ToUnixTimeSeconds(),
            photo = "",
            status = "request_created"
        };
        string resp = _cache.Set( request.id, JsonConvert.SerializeObject( request )
            , DateTimeOffset.Now.AddSeconds( _configuration.GetValue<int>( "AppSettings:CacheExpiresInSeconds", 300 ) ) );
        return (true, request, null);
    }
    public void SelfieRequestRetrieved( HttpRequest httpRequest ) {
        _httpRequest = httpRequest;
        if (_httpRequest.Query.ContainsKey( "callbackUrl" )) {
            string[] parts = _httpRequest.Query["callbackUrl"].ToString().Split( "/" );
            string id = parts[parts.Length - 1];
            if (_cache.TryGetValue( id, out string requestState )) {
                JObject reqState = JObject.Parse( requestState );
                reqState["status"] = "request_retrieved";
                _cache.Set( id, JsonConvert.SerializeObject( reqState ), DateTimeOffset.Now.AddSeconds( _cacheTtl ) );
            }
        }
    }
} // cls
