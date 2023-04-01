using AspNetCoreFido2MFA.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(opts =>
{
    // we don't care about antiforgery in the demo
    opts.Conventions.ConfigureFilter(new IgnoreAntiforgeryTokenAttribute());
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Fido2MfaDb")));
// Use the in-memory implementation of IDistributedCache.
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    // Set a short timeout for easy testing.
    options.IdleTimeout = TimeSpan.FromMinutes(2);
    options.Cookie.HttpOnly = true;
    // Strict SameSite mode is required because the default mode used
    // by ASP.NET Core 3 isn't understood by the Conformance Tool
    // and breaks conformance testing
    options.Cookie.SameSite = SameSiteMode.Unspecified;
});

builder.Services.AddFido2(options =>
    {
        options.ServerDomain = builder.Configuration["Fido2:serverDomain"];
        options.ServerName = "FIDO2 Test";
        options.Origins = builder.Configuration.GetSection("Fido2:origins").Get<HashSet<string>>();
        options.TimestampDriftTolerance = builder.Configuration.GetValue<int>("Fido2:timestampDriftTolerance");
        options.MDSCacheDirPath = builder.Configuration["Fido2:MDSCacheDirPath"];
    })
    .AddCachedMetadataService(config =>
    {
        config.AddFidoMetadataRepository(httpClientBuilder =>
        {
            //TODO: any specific config you want for accessing the MDS
        });
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseSession();
app.UseStaticFiles();
app.UseRouting();
app.UseEndpoints(endpoints =>
{
    endpoints.MapRazorPages();
    endpoints.MapControllers();
});

app.Run();