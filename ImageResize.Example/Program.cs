using ImageResize.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddImageResize(o =>
{
    // Configure to serve images from the images directory
    o.ContentRoot = Path.Combine(builder.Environment.WebRootPath ?? "wwwroot", "images");
    // Configure cache directory
    o.CacheRoot = Path.Combine(builder.Environment.WebRootPath ?? "wwwroot", "_imgcache");
});

// Add services to the container.
builder.Services.AddRazorPages();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Image resize middleware must be before routing to intercept requests
app.UseImageResize();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapRazorPages()
    .WithStaticAssets();

app.Run();