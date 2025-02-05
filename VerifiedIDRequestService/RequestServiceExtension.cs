using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Entra.VerifiedID; 
public static class RequestServiceExtension {
    public static IServiceCollection AddVerifiedIDRequestService( this IServiceCollection services ) {
        services.AddScoped<IRequestService, RequestService>();
        // generate an api-key on startup that we can use to validate callbacks
        System.Environment.SetEnvironmentVariable( "API-KEY", Guid.NewGuid().ToString() );
        return services;
    }
}
