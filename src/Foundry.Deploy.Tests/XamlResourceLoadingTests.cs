// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Views;

namespace Foundry.Deploy.Tests;

public sealed class XamlResourceLoadingTests
{
    [Fact]
    public void WizardView_LoadsApplicationResourcesWithoutMissingStaticResources()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new App();
                application.InitializeComponent();
                _ = new WizardView();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);

        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }
}
