using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Authorization;
using WoodgroveEmployee.Helpers;
using WoodgroveEmployee.Models;
using System.Security.Cryptography;
using Microsoft.Entra.VerifiedID;

namespace WoodgroveEmployee.Controllers
{
    //[Route("api/[controller]/[action]")]
    public class HomeController : Controller
    {
        protected IMemoryCache _cache;
        protected readonly ILogger<HomeController> _log;
        private IConfiguration _configuration;
        private string[] _queryStringParameters = new string[] { "firstName", "lastName" };
        private IRequestService _requestService;

        public HomeController(IConfiguration configuration, IMemoryCache memoryCache, ILogger<HomeController> log, IRequestService requestService)
        {
            _configuration = configuration;
            _cache = memoryCache;
            _log = log;
            _requestService = requestService;
        }
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // some helper functions
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        protected bool IsMobile() {
            string userAgent = this.Request.Headers["User-Agent"];
            return (userAgent.Contains( "Android" ) || userAgent.Contains( "iPhone" ));
        }

        private Dictionary<string, string> GetUserClaimsFromSession() {
            Dictionary<string, string> claims = new Dictionary<string, string>();
            claims.Add( "firstName", "Matthew" );
            claims.Add( "lastName", "Michael" );
            string jsonString = this.Request.HttpContext.Session.GetString( "userClaims" );
            if (!string.IsNullOrWhiteSpace( jsonString )) {
                Dictionary<string, string> userClaims = JsonConvert.DeserializeObject<Dictionary<string, string>>( jsonString );
                userClaims.ToList().ForEach( x => claims[x.Key] = x.Value );
            }
            return claims;
        }
        private string SetOnboardingUserClaimsToSession( Dictionary<string, string> claims ) {
            string jsonString = JsonUtils.ToJsonString( claims );
            this.Request.HttpContext.Session.SetString( "userClaims", jsonString );
            return jsonString;
        }

        private Dictionary<string,string> SetOnboardingUserViewData( Dictionary<string, string> claims = null) {
            if ( claims  == null ) {
                claims = GetUserClaimsFromSession();
            }
            foreach( var claim in claims ) {
                ViewData[claim.Key] = claim.Value;
            }
            ViewData["IsMobile"] = IsMobile();
            return claims;
        }

        private Dictionary<string, string> GetClaimsMappingForPresentedVC( ClaimsIssuer[] verifiedCredentialsData, out ClaimsIssuer verifiedCredentialData ) {
            verifiedCredentialData = null;
            foreach (var vc in verifiedCredentialsData) {
                foreach (var credentialType in vc.type) {
                    string mapping = _configuration.GetValue<string>( $"VerifiedID:{credentialType}-{vc.issuer}-claimsMapping", null );
                    if (mapping != null) {
                        verifiedCredentialData = vc;
                        return JsonConvert.DeserializeObject<Dictionary<string, string>>( mapping );
                    }
                }
            }
            return null;
        }
        private WoodgroveCredential MapClaimsFromPresentedVCToWoodgroveCredential( ClaimsIssuer[] verifiedCredentialsData ) {
            // get claims mapping rules from config
            Dictionary<string, string> claimsMapping = GetClaimsMappingForPresentedVC( verifiedCredentialsData, out ClaimsIssuer vc );
            if (claimsMapping == null) { 
                return null; // this shouldn't happen unless your appsettings.json is wrong and you accepted a VC you are not prepared for
            }
            WoodgroveCredential wvc = new WoodgroveCredential() {
                firstName = vc.claims[claimsMapping["firstName"]],
                lastName = vc.claims[claimsMapping["lastName"]]
            };
            if (claimsMapping.ContainsKey( "photo" )) {
                wvc.photo = vc.claims[claimsMapping["photo"]];
            }
            if (claimsMapping.ContainsKey( "address" )) {
                wvc.address = vc.claims[claimsMapping["address"]];
            }
            // claims we didn't get from the presented VC - static from config in our demo case
            Dictionary<string, string> staticClaims = JsonConvert.DeserializeObject<Dictionary<string, string>>( _configuration["VerifiedID:staticClaims"] );
            wvc.company = staticClaims["company"];
            wvc.department = staticClaims["department"];
            wvc.title = staticClaims["title"];
            int empNoLength = 6;
            int empNo = RandomNumberGenerator.GetInt32( 1, int.Parse( "".PadRight( empNoLength, '9' ) ) );
            wvc.documentNumber = string.Format( "WG-{0:D" + empNoLength + "}", empNo );

            return wvc;
        }
        private CallbackEvent GetPresentationVerifiedEvent() {
            string requestId = this.Request.HttpContext.Session.GetString( "requestId" );
            if ( string.IsNullOrWhiteSpace(requestId) ) { 
                return null;
            }
            CallbackEvent callback = _requestService.GetCachedRequestEvent( requestId );
            if (callback != null && callback.requestStatus != "presentation_verified") {
                callback = null;
            }
            return callback;
        }
        private IssuanceRequest SetOnboardedUserClaimsForIssuance( IssuanceRequest request ) {
            string json = this.Request.HttpContext.Session.GetString( "verifiedIdentity" );
            Dictionary<string, string> wvcClaims = JsonConvert.DeserializeObject<Dictionary<string, string>>( json );
            if (request.claims == null) {
                request.claims = new Dictionary<string, string>();
            }
            foreach (var claim in wvcClaims) {
                request.claims.Add( claim.Key, claim.Value );
            }
            return request;
        }

        private void SetViewSectionName() {
            if ( this.Request.Path.ToString().StartsWith("/alumni/") ) {
                ViewData["sectionName"] = "Employee Alumni Demo";
            } else {
                ViewData["sectionName"] = "Employee Onboarding Demo";
            }
        }
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // UI handlers
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        [AllowAnonymous]
        [ResponseCache( Duration = 0, Location = ResponseCacheLocation.None, NoStore = true )]
        public IActionResult Error() {
            return View( new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier } );
        }

        [AllowAnonymous]
        public IActionResult Index() {
            return Redirect("/onboarding/landing");
        }
        [AllowAnonymous]
        [HttpGet( "/signout" )]
        public IActionResult Signout() {
            this.Request.HttpContext.Session.Remove( "requestId" );
            this.Request.HttpContext.Session.Remove( "verifiedIdentity" );
            this.Request.HttpContext.Session.Remove( "alumniUser" );
            ViewData["IsVerified"] = false;
            ViewData["IsAlumniVerified"] = false;
            return Redirect( "/onboarding/landing" );
        }
        [AllowAnonymous]
        [HttpGet( "/landing" )]
        public IActionResult landing() {
            return Redirect( "/onboarding/landing" );
        }
        /// <summary>
        /// Landing page where you can choose your name for demo purposes
        /// If user already have an IDV VC, the name doesn't matter and inputted name may be wrong
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet( "/onboarding/landing" )]
        public IActionResult OnboardingLanding() {
            SetViewSectionName();
            Dictionary<string, string> claims = SetOnboardingUserViewData();
            bool changed = false;
            foreach (string qpName in _queryStringParameters) {
                if (this.Request.Query.ContainsKey( qpName )) {
                    claims[qpName] = this.Request.Query[qpName].ToString();
                    changed = true;
                }
            }
            if (changed) {
                SetOnboardingUserViewData( claims );
            }
            return View();
        }

        [AllowAnonymous]
        [HttpGet( "/verification" )]
        public IActionResult verification() {
            return Redirect( "/onboarding/verification" );
        }
        /// <summary>
        /// Page that asks user to go and get an IDV VC and then present it back
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet( "/onboarding/verification" )]
        public IActionResult OnboardingVerification() {
            SetViewSectionName();
            Dictionary<string, string> claims = SetOnboardingUserViewData();
            if ( (ViewData.ContainsKey("IsVerified") && (bool)ViewData["IsVerified"]) || CheckAlumniVerified() ) {
                return Redirect( "/dashboard" );
            }
            ViewData["returnUrl"] = this.Request.GetDisplayUrl().Split("?")[0];
            return View();
        }

        [AllowAnonymous]
        [HttpGet( "/dashboard" )]
        public IActionResult dashboard() {
            return Redirect( "/onboarding/dashboard" );
        }
        /// <summary>
        /// Page after IDV VC presentation where user gets issued a Workplace Credential VC
        /// and continues to choose a laptop at Proseware
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet( "/onboarding/dashboard" )]
        public IActionResult OnboardingDashboard() {
            SetViewSectionName();
            bool isVerified = false;
            WoodgroveCredential wvc = null;
            // 1) if we've already mapped presented VC to WoodgroveCredential we have in the session (presented VC may have expired in cache)
            // 2) We have a presented VC in cache and can map it to a WoodgroveCredential we can store in the session
            string json = this.Request.HttpContext.Session.GetString( "verifiedIdentity" );
            if ( !string.IsNullOrWhiteSpace(json) ) {
                wvc = JsonConvert.DeserializeObject<WoodgroveCredential>( json );
            } else {
                CallbackEvent callback = GetPresentationVerifiedEvent();
                if (callback != null) {
                    wvc = MapClaimsFromPresentedVCToWoodgroveCredential( callback.verifiedCredentialsData );
                    if ( wvc != null ) {
                        this.Request.HttpContext.Session.SetString( "verifiedIdentity", JsonUtils.ToJsonString( wvc ) );
                    }
                }
            }
            if (wvc != null) {
                isVerified = true;
                ViewData["firstName"] = wvc.firstName;
                ViewData["lastName"] = wvc.lastName;
                ViewData["address"] = wvc.address;
                ViewData["photo"] = Base64Url.DecodeString( wvc.photo ); // VC holds jpeg in base64 UrlEncoded state
            }
            if ( !isVerified ) {
                return Redirect( "/onboarding/verification" );
            }   
            ViewData["IsVerified"] = isVerified;
            ViewData["IsMobile"] = IsMobile();
            return View();
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // UI handlers
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        [AllowAnonymous]
        [HttpGet( "/alumni" )]
        public IActionResult AlumniIndex() {
            return Redirect( "/alumni/landing" );
        }

        /// <summary>
        /// Landing page where you can choose your name for demo purposes
        /// If user already have an IDV VC, the name doesn't matter and inputted name may be wrong
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet( "/alumni/landing" )]
        public IActionResult AlumniLanding() {
            SetViewSectionName();
            if (CheckAlumniVerified()) {
                return Redirect("/alumni/dashboard");
            }
            return View();
        }

        /// <summary>
        /// Page after IDV VC presentation where user gets issued a Workplace Credential VC
        /// and continues to choose a laptop at Proseware
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet( "/alumni/dashboard" )]
        public IActionResult AlumniDashboard( string id ) {
            SetViewSectionName();
            bool isVerified = CheckAlumniVerified();
            if ( !isVerified ) { 
                return Redirect("/alumni/landing");
            }
            ViewData["IsVerified"] = isVerified;
            ViewData["IsMobile"] = IsMobile();
            return View();
        }

        private bool CheckAlumniVerified() {
            Dictionary<string,string> claims = null;
            bool rc = false;
            // 1) we have already the Alumni VC in the session
            // 2) we don't but the Alumni Woodgrove VC have been presented
            string json = this.Request.HttpContext.Session.GetString( "alumniUser" );
            if ( json != null ) {
                claims = JsonConvert.DeserializeObject<Dictionary<string, string>>( json );
            } else {
                // during createPresentationRequest, we store the requestId in the session.
                // we should be able to retrieve a 'presentation_verified' event here
                CallbackEvent callback = GetPresentationVerifiedEvent();
                if (callback != null
                    && Array.IndexOf( callback.verifiedCredentialsData[0].type, _configuration["VerifiedID:FTECredentialType"] ) >= 0
                    && callback.verifiedCredentialsData[0].issuer == _configuration["VerifiedID:DidAuthority"]) {
                    claims = (Dictionary<string,string>)callback.verifiedCredentialsData[0].claims;
                    this.Request.HttpContext.Session.SetString( "alumniUser", JsonUtils.ToJsonString( claims ) );
                }
            }
            if ( claims != null ) {
                foreach (var claim in claims) {
                    ViewData[claim.Key] = claim.Value;
                }
                rc = true;
            }
            ViewData["IsAlumniVerified"] = rc;
            return rc;
        }
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Common API Handlers
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Called by /landing page to store firstName/lastName
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost( "/api/save-profile" )]
        public async Task<IActionResult> SaveProfile() {
            _log.LogTrace( this.HttpContext.Request.GetDisplayUrl() );
            string body = await new System.IO.StreamReader( this.Request.Body ).ReadToEndAsync();
            JObject json = JObject.Parse( body );
            Dictionary<string, string> userClaims = GetUserClaimsFromSession();
            foreach (string claimName in _queryStringParameters) {
                if (json.ContainsKey( claimName )) {
                    if ( userClaims.ContainsKey( claimName) ) {
                        userClaims[claimName] = json[claimName].ToString();
                    } else {
                        userClaims.Add( claimName, json[claimName].ToString() );
                    }
                } else {
                    userClaims.Add( claimName, null );
                }
            }
            string jsonString = SetOnboardingUserClaimsToSession( userClaims );
            _log.LogTrace( jsonString );
            return new ContentResult { ContentType = "application/json", Content = jsonString };
        }
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Issuance API Handlers
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method is called from the UI to initiate the issuance of the verifiable credential
        /// </summary>
        /// <returns>JSON object with the address to the presentation request and optionally a QR code and a state value which can be used to check on the response status</returns>
        [AllowAnonymous]
        [HttpGet( "/api/issuer/issuance-request" )]
        public async Task<ActionResult> IssuanceRequest() {
            _log.LogTrace( this.HttpContext.Request.GetDisplayUrl() );
            try {
                if (!_requestService.ValidateTenant( out string errmsg )) {
                    _log.LogError( errmsg );
                    return BadRequest( new { error = "400", error_description = errmsg } );
                }
                IssuanceRequest request = SetOnboardedUserClaimsForIssuance( _requestService.CreateIssuanceRequest( this.Request ) );
                var res = await _requestService.PostIssuanceRequest( request );
                if ( res.success ) {
                    return new ContentResult { ContentType = "application/json", Content = JsonUtils.ToJsonString(res.response) };
                } else {
                    return BadRequest( new { error = "400", error_description = $"{res.error.error.message} - {res.error.error.innererror.code}: {res.error.error.innererror.message}" } );
                }
            } catch (Exception ex) {
                return BadRequest( new { error = "400", error_description = "Technical error: " + ex.Message } );
            }
        }
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Presentation API Handlers
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Called from /verification page to ask for an IDV VC to be presented
        /// Set a name for sourcePhotoClaimName in appsettings.json to force a Face Check during presentation. Leave blank to skip
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet( "/api/verifier/presentation-request" )]
        public async Task<ActionResult> PresentationRequest() {
            _log.LogTrace( this.Request.GetDisplayUrl() );
            try {
                string sourcePhotoClaimName = _configuration["VerifiedID:sourcePhotoClaimName"];
                List<string> acceptedIssuers = null;
                string credentialType = null;
                // If in Alumni pages, then we ask for a Woodgrove VC to be presented
                // else we ask for a VerifiedIdentity VC to be presented
                if (this.Request.Headers.ContainsKey("Referer") && this.Request.Headers["Referer"][0].Contains("/alumni/") ) {
                    credentialType = _configuration["VerifiedID:FTECredentialType"];
                    acceptedIssuers = new List<string>( _configuration["VerifiedID:DidAuthority"].Split( "\t" ) ); // just using Split to create a list
                } else {
                    acceptedIssuers = new List<string>( _configuration["VerifiedID:acceptedIssuers"].Split( "," ) );
                }
                PresentationRequest request = _requestService.CreatePresentationRequest( this.Request, null, credentialType, acceptedIssuers, sourcePhotoClaimName );
                var res = await _requestService.PostPresentationRequest( request );
                if ( res.success ) {
                    this.Request.HttpContext.Session.SetString( "requestId", res.response.id );
                    return new ContentResult { ContentType = "application/json", Content = JsonUtils.ToJsonString( res.response ) };
                } else {
                    _log.LogError( "Error calling Verified ID API: " + res.response );
                    return BadRequest( new { error = "400", error_description = $"{res.error.error.message} - {res.error.error.innererror.code}: {res.error.error.innererror.message}" } );
                }
            } catch (Exception ex) {
                _log.LogError( "Exception: " + ex.Message );
                return BadRequest( new { error = "400", error_description = "Exception: " + ex.Message } );
            }
        }
    } // cls
} // ns
