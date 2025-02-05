using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Authorization;
using TrueIdentityIncV2.Helpers;
using TrueIdentityIncV2.Models;
using System.Security.Cryptography;
using Azure.Core;
using Microsoft.Entra.VerifiedID;

namespace TrueIdentityIncV2.Controllers
{
    //[Route("api/[controller]/[action]")]
    public class HomeController : Controller
    {
        protected IMemoryCache _cache;
        protected readonly ILogger<HomeController> _log;
        private IConfiguration _configuration;
        private string[] _inputClaimNames = new string[] { "firstName", "lastName", "photo", "address", "dateOfBirth"
                                                        , "documentType", "documentNumber", "gender", "nationality" };
        private IRequestService _requestService;

        public HomeController(IConfiguration configuration, IMemoryCache memoryCache, ILogger<HomeController> log, IRequestService requestService )
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

        private string SetUserClaimsToSession( Dictionary<string,string> claims ) {
            string jsonString = JsonConvert.SerializeObject( claims, Formatting.None );
            this.Request.HttpContext.Session.SetString( "userClaims", jsonString );
            return jsonString;
        }
        private Dictionary<string, string> GetUserClaimsFromSession() {
            int docNoLength = 9;
            int docNo = RandomNumberGenerator.GetInt32( 1, int.Parse( "".PadRight( docNoLength, '9' ) ) );

            Dictionary<string, string> claims = new Dictionary<string, string>();
            claims.Add( "firstName", "Matthew" );
            claims.Add( "lastName", "Michael" );
            claims.Add( "address", "2345 Anywhere Street, Your City, NY 12345" );
            claims.Add( "selfie", "Verified selfie" );
            claims.Add( "ageverified", "Older than 21" );
            claims.Add( "verification", "Fully Verified (demo)" );
            claims.Add( "scanneddoc", "NY State Drivers License" );
            claims.Add( "photo", null );
            claims.Add( "dateOfBirth", "1980-01-01" );
            claims.Add( "documentType", "DL" );
            claims.Add( "documentNumber", "AB" + string.Format( "{0:D" + docNoLength + "}", docNo ) );
            claims.Add( "gender", "M" );
            claims.Add( "nationality", "USA" );
            string jsonString = this.Request.HttpContext.Session.GetString( "userClaims" );
            if (!string.IsNullOrWhiteSpace( jsonString )) {
                Dictionary<string, string> userClaims = JsonConvert.DeserializeObject<Dictionary<string, string>>( jsonString );
                userClaims.ToList().ForEach( x => claims[x.Key] = x.Value );
            }
            return claims;
        }
        private IssuanceRequest SetClaims( IssuanceRequest request ) {
            request.claims = GetUserClaimsFromSession();
            DateTime dateOfBirth = DateTime.Parse( request.claims["dateOfBirth"]);
            int years = 18;
            if ( request.claims["nationality"].ToUpperInvariant() == "USA") {
                years = 21;
            }
            DateTime dateLegalAge = dateOfBirth.AddYears(years);
            if ( DateTime.Now >= dateLegalAge ) {
                request.claims["ageverified"] = $"Older than {years}";
            } else {
                request.claims["ageverified"] = $"Younger than {years}";
            }
            request.claims["scanneddoc"] = request.claims["documentType"] == "dl" ? "Drivers License" : "Passport";
            return request;
        }
        private Dictionary<string,string> SetUserViewData( Dictionary<string, string> claims = null) {
            if ( claims  == null ) {
                claims = GetUserClaimsFromSession();
            }
            foreach( var claim in claims ) {
                ViewData[claim.Key] = claim.Value;
            }
            ViewData["IsMobile"] = IsMobile();
            return claims;
        }
        private void StoreReturnUrl() {
            if (this.Request.Query.ContainsKey( "returnUrl" )) {
                string returnUrl = this.Request.Query["returnUrl"].ToString();
                Uri returnUri = new Uri( returnUrl );
                returnUrl = $"{returnUri.Scheme}{Uri.SchemeDelimiter}{returnUri.Host}{returnUri.AbsolutePath}?trueIdVerified=true";
                this.Request.HttpContext.Session.SetString( "returnUrl", returnUrl );
            }
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // UI handlers
        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>
        /// Return HTML page that runs on user's mobile phone to take a selfie
        /// We update the cache to say "request_retrieved"
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet("/selfie")]
        public IActionResult selfie() {
            string html = null;
            if (!_cache.TryGetValue( "selfie.html", out html )) {
                string path = Path.Combine( Path.GetDirectoryName( System.Reflection.Assembly.GetEntryAssembly().Location ), "wwwroot\\selfie.html" );
                html = System.IO.File.ReadAllText( path );
                _cache.Set( "selfie.html", html );
            }
            _requestService.SelfieRequestRetrieved( this.Request );
            return Content( html, "text/html; charset=utf-8");
        }
        /// <summary>
        /// Start page
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        public IActionResult Index() {
            Dictionary<string,string> claims = SetUserViewData();
            bool changed = false;
            foreach( string qpName in _inputClaimNames ) {
                if (this.Request.Query.ContainsKey( qpName )) {
                    claims[qpName] = this.Request.Query[qpName].ToString();
                    changed = true;
                }
            }
            if ( changed ) {
                SetUserClaimsToSession( claims );
                SetUserViewData( claims );
            }
            StoreReturnUrl();
            return View();
        }
        /// <summary>
        /// Page where you take a selfie, set your data and fake uploading of docs
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpGet( "/verify-credential" )]
        public IActionResult VerifyCredential() {
            Dictionary<string, string> claims = SetUserViewData();
            string returnUrl = this.Request.HttpContext.Session.GetString( "returnUrl" );
            if ( string.IsNullOrEmpty( returnUrl ) ) {
                ViewData["returnUrl"] = "";
            } else {
                ViewData["returnUrl"] = $"{returnUrl}&firstName={claims["firstName"]}&lastName={claims["lastName"]}";
            }
            return View();
        }

        [AllowAnonymous]
        [ResponseCache( Duration = 0, Location = ResponseCacheLocation.None, NoStore = true )]
        public IActionResult Error() {
            return View( new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier } );
        }

        //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // API Handlers
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
                IssuanceRequest request = SetClaims( _requestService.CreateIssuanceRequest( this.Request ) );
                var res = await _requestService.PostIssuanceRequest( request );
                if (res.success) {
                    return new ContentResult { ContentType = "application/json", Content = JsonUtils.ToJsonString( res.response ) };
                } else {
                    return BadRequest( new { error = "400", error_description = $"{res.error.error.message} - {res.error.error.innererror.code}: {res.error.error.innererror.message}" } );
                }
            } catch (Exception ex) {
                return BadRequest( new { error = "400", error_description = "Technical error: " + ex.Message } );
            }
        }

        /// <summary>
        /// Creates the QR code for the selfie request
        /// </summary>
        /// <returns></returns>
        [HttpGet( "/api/issuer/selfie-request" )]
        public ActionResult SelfieRequest() {
            _log.LogTrace( this.HttpContext.Request.GetDisplayUrl() );
            try {
                var res = _requestService.CreateSelfieRequest( this.Request ).Result;
                if ( !res.success ) {
                    return BadRequest( new { error = "400", error_description = res.error } );
                }
                return new ContentResult { StatusCode = (int)HttpStatusCode.Created, ContentType = "application/json", Content = JsonUtils.ToJsonString( res.request) };
            } catch (Exception ex) {
                return BadRequest( new { error = "400", error_description = ex.Message } );
            }
        }

        /// <summary>
        ///  API to save whatever the user enter as personal data
        /// </summary>
        /// <returns></returns>
        [AllowAnonymous]
        [HttpPost( "/api/issuer/save-profile" )]
        public async Task<IActionResult> SaveProfile() {
            _log.LogTrace( this.HttpContext.Request.GetDisplayUrl() );
            string body = await new System.IO.StreamReader( this.Request.Body ).ReadToEndAsync();
            JObject json = JObject.Parse( body );
            Dictionary<string, string> userClaims = new Dictionary<string, string>();
            foreach ( string claimName in _inputClaimNames) {
                if ( json.ContainsKey( claimName ) ) {
                    userClaims.Add( claimName, json[claimName].ToString() );
                } else {
                    userClaims.Add( claimName, null );
                }
            }
            string jsonString = SetUserClaimsToSession( userClaims );
            _log.LogTrace( jsonString );
            return new ContentResult { ContentType = "application/json", Content = jsonString };
        }

    } // cls
} // ns
