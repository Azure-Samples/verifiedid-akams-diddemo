using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Entra.VerifiedID;

public class Program {
    public static void Main( string[] args ) {
        var builder = WebApplication.CreateBuilder( args );
        builder.Services.AddControllersWithViews();
        builder.Services.AddRazorPages();
        builder.Services.AddHttpClient();
        builder.Services.AddSession( options => {
            options.IdleTimeout = TimeSpan.FromMinutes( 5 );//You can set Time   
            options.Cookie.IsEssential = true;
            options.Cookie.HttpOnly = true;
        } );
        builder.Services.Configure<KestrelServerOptions>( options => {
            options.AllowSynchronousIO = true;
        } );
        builder.Services.Configure<IISServerOptions>( options => {
            options.AllowSynchronousIO = true;
        } );
        builder.Services.AddVerifiedIDRequestService();

        var app = builder.Build();
        app.UseForwardedHeaders( new ForwardedHeadersOptions {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
        } );
        if (!app.Environment.IsDevelopment()) {
            app.UseExceptionHandler( "/Home/Error" );
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseSession();
        app.MapControllerRoute( name: "default", pattern: "{controller=Home}/{action=Index}/{id?}" );
        app.MapRazorPages();

        app.Run();
    }
}
