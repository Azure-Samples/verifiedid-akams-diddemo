using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using System.Net;

namespace Microsoft.Entra.VerifiedID; 
public interface IRequestService {
    public enum RequestType {
        Unknown,
        Presentation,
        Issuance,
        Selfie
    };

    public CallbackEvent GetCachedRequestEvent( string requestId );
    public Task<(bool success, RequestResponse? response, RequestError? error)> PostIssuanceRequest( IssuanceRequest request );
    public IssuanceRequest CreateIssuanceRequest( HttpRequest httpRequest, string stateId = null );
    public IssuanceRequest SetExpirationDate( IssuanceRequest request );
    public IssuanceRequest SetPinCode( IssuanceRequest request, string pinCode = null );
    public bool ValidateTenant( out string errmsg );
    public Task<(bool success, RequestResponse? response, RequestError? error)> PostPresentationRequest( PresentationRequest request );
    public PresentationRequest CreatePresentationRequest( HttpRequest httpRequest, string stateId = null
                            , string credentialType = null, List<string> acceptedIssuers = null, string faceCheckPhotoClaim = null );
    public PresentationRequest AddRequestedCredential( PresentationRequest request
                                            , string credentialType, List<string> acceptedIssuers
                                            , bool allowRevoked = false, bool validateLinkedDomain = true );
    public void AddFaceCheck( RequestedCredential requestedCredential, string photoClaimName = null );
    public bool IsFaceCheckRequested( PresentationRequest request );
    public Task<(bool success, SelfieRequest? request, string? error)> CreateSelfieRequest( HttpRequest httpRequest );
    public void SelfieRequestRetrieved( HttpRequest httpRequest );

    public Task<(HttpStatusCode statusCode, string? errorMessage)> HandleRequestCallback( HttpRequest httpRequest, RequestType requestType, string body );
    public Task<(bool success, JObject? result)> PollRequestStatus( HttpRequest httpRequest );
    public Task<(HttpStatusCode statusCode, CallbackEvent? callback, string? errorMessage)> HandleSelfiePostback( HttpRequest httpRequest, string id );
}
