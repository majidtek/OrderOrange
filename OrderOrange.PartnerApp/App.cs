using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using OrderOrange.PartnerApp.Components;

namespace OrderOrange.PartnerApp;

/// <summary>One window, one full-screen WebView running the portal.</summary>
public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var web = new BlazorWebView { HostPage = "wwwroot/index.html" };
        web.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(HybridRoot) });
        web.RootComponents.Add(new RootComponent { Selector = "head::after", ComponentType = typeof(HeadOutlet) });

        var page = new ContentPage { Content = web, Title = "OrderOrange Partner" };
        return new Window(page) { Title = "OrderOrange Partner" };
    }
}
