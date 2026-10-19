using System;
using System.Threading.Tasks;
using _855Media.Core.Licensing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace _855Media.Framework;

public abstract class ViewModelBase : ObservableObject, IDisposable
{
    internal ILicenseService? ModuleLicense { get; set; }

    protected bool CanUseSubscription() =>
        ModuleLicense?.IsFeatureAllowed(LicenseFeature.All) == true;

    protected void RequireSubscription(LicenseFeature feature = LicenseFeature.All)
    {
        if (ModuleLicense?.IsFeatureAllowed(feature) != true)
            throw new InvalidOperationException(
                "An active subscription is required. Open Settings → Subscription to activate or renew."
            );
    }

    ~ViewModelBase() => Dispose(false);

    protected void OnAllPropertiesChanged() => OnPropertyChanged(string.Empty);

    public virtual Task InitializeAsync() => Task.CompletedTask;

    protected virtual void Dispose(bool disposing) { }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
