// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Tests;

/// <summary>
/// Runs alone: the tests in it change the process-wide <see cref="System.Globalization.CultureInfo.DefaultThreadCurrentCulture"/> and
/// <see cref="System.Globalization.CultureInfo.DefaultThreadCurrentUICulture"/> (the localization services set them on every language
/// change), which every thread created meanwhile by a parallel test would inherit.
/// </summary>
[CollectionDefinition(nameof(ProcessCultureCollection), DisableParallelization = true)]
public sealed class ProcessCultureCollection;
