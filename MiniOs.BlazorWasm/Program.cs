using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MiniOs.BlazorWasm;
using MiniOs.BlazorWasm.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

#if DEBUG
builder.Services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri("http://localhost:8888") });
#else
builder.Services.AddSingleton(sp => new HttpClient { BaseAddress = new Uri("https://mos.xcssa.com/") });
#endif
builder.Services.AddSingleton<MiniOsTerminalHost>();

await builder.Build().RunAsync();
