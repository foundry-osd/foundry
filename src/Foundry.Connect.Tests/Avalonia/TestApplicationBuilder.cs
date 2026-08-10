// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using global::Avalonia;
using global::Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Foundry.Connect.Tests.Avalonia.TestApplicationBuilder))]

namespace Foundry.Connect.Tests.Avalonia;

public static class TestApplicationBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<TestApp>()
        .UseHarfBuzz()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = false
        });
}
