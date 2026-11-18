// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using Foundry.WinPE.Tests.Support;
using Foundry.WinPE.Ui;
using Xunit;

namespace Foundry.WinPE.Tests.Ui;

public sealed class UiCrashWindowTests
{
    [Theory]
    [InlineData(true, true, (int)UiCrashChoice.Relaunch)]
    [InlineData(true, false, (int)UiCrashChoice.Close)]
    [InlineData(true, null, (int)UiCrashChoice.Close)]
    [InlineData(false, true, (int)UiCrashChoice.Close)]
    [InlineData(false, null, (int)UiCrashChoice.Close)]
    public void OnlyTheRelaunchButtonOfAnAllowedWindowRelaunches(bool relaunchAllowed, bool? dialogResult, int expected)
        => Assert.Equal((UiCrashChoice)expected, UiCrashWindow.ToChoice(relaunchAllowed, dialogResult));

    [Fact]
    public void EveryCultureFormatsTheLogFolder()
    {
        var strings = new ResourceManager("Foundry.WinPE.Strings.Resources", typeof(UiCrashWindow).Assembly);
        string[] cultures = Directory.GetDirectories(Path.Combine(RepositoryRoot.Find(), "src", "Foundry.WinPE", "Strings"))
            .Select(path => Path.GetFileName(path))
            .ToArray();

        Assert.True(cultures.Length > 30);
        Assert.All(cultures, name => Assert.Contains(@"X:\Foundry\Logs",
            string.Format(CultureInfo.InvariantCulture, strings.GetString("UiCrash.LogFolder", CultureInfo.GetCultureInfo(name))!, @"X:\Foundry\Logs")));
    }
}
