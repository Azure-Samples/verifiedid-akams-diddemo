using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;

namespace Microsoft.Entra.VerifiedID;

[Route("api/[action]")]
[ApiController]
public class CallbackController : Controller
{
    private readonly ILogger<CallbackController> _log;
    private IRequestService _requestService;
    public CallbackController(ILogger<CallbackController> log, IRequestService requestService)
    {
        _log = log;
        _requestService = requestService;
    }

    /// <summary>
    /// Issuance callback endpoint
    /// </summary>
    /// <returns></returns>
    [AllowAnonymous]
    [HttpPost( "/api/issuer/issuecallback" )]
    public async Task<ActionResult> IssuanceCallback() {
        _log.LogTrace( this.Request.GetDisplayUrl() );
        var res = await _requestService.HandleRequestCallback( this.Request, IRequestService.RequestType.Issuance, null );
        if ( res.statusCode == HttpStatusCode.OK ) {
            return new OkResult();
        } else {
            return BadRequest( new { error = "400", error_description = res.errorMessage } );
        }
    }

    /// <summary>
    /// Presentation callback endpoint
    /// </summary>
    /// <returns></returns>
    [AllowAnonymous]
    [HttpPost( "/api/verifier/presentationcallback" )]
    public async Task<ActionResult> PresentationCallback() {
        _log.LogTrace( this.Request.GetDisplayUrl() );
        var res = await _requestService.HandleRequestCallback( this.Request, IRequestService.RequestType.Presentation, null );
        if (res.statusCode == HttpStatusCode.OK) {
            return new OkResult();
        } else {
            return BadRequest( new { error = "400", error_description = res.errorMessage } );
        }
    }

    /// <summary>
    /// Endpoint called by UI to poll request status
    /// </summary>
    /// <returns></returns>
    [AllowAnonymous]
    [HttpGet( "/api/request-status" )]
    public async Task<ActionResult> RequestStatus() {
        _log.LogTrace( this.Request.GetDisplayUrl() );
        try {
            var res = await _requestService.PollRequestStatus( this.Request );
            if ( !res.success ) {
                return BadRequest( new { error = "400", error_description = JsonConvert.SerializeObject( res.result ) } );
            }
            return new ContentResult { ContentType = "application/json", Content = JsonConvert.SerializeObject( res.result ) };
        } catch (Exception ex) {
            _log.LogTrace( $"error 400 - {ex.Message}" );
            return BadRequest( new { error = "400", error_description = ex.Message } );
        }
    }

    /// <summary>
    /// Endpoint called by user's mobile when submitting a selfie
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [AllowAnonymous]
    [HttpPost( "/api/issuer/selfie/{id}" )]
    public async Task<ActionResult> setSelfie( string id ) {
        _log.LogTrace( this.Request.GetDisplayUrl() );
        try {
            var res = await _requestService.HandleSelfiePostback( this.Request, id );
            if (res.statusCode != HttpStatusCode.OK) {
                return BadRequest( new { error = "400", error_description = res.errorMessage } );
            }
            var res2 = await _requestService.HandleRequestCallback( this.Request, IRequestService.RequestType.Selfie, JsonConvert.SerializeObject( res.callback ) );
            if (res2.statusCode == HttpStatusCode.OK) {
                return new OkResult();
            } else {
                return BadRequest( new { error = "400", error_description = res2.errorMessage } );
            }
        } catch (Exception ex) {
            _log.LogTrace( $"error 400 - {ex.Message}" );
            return BadRequest( new { error = "400", error_description = ex.Message } );
        }
    }

} // cls
