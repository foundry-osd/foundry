// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.CodeAnalysis;

namespace Foundry.Services.Shell;

/// <summary>Creates page-owned view models without retaining their disposal in the root container.</summary>
/// <remarks>Injected application services remain container-owned; the page disposes its view model.</remarks>
internal sealed class PageViewModelFactory(IServiceProvider services)
{
    public T Create<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IDisposable => ActivatorUtilities.CreateInstance<T>(services);
}
