// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Controls;

/// <summary>Functions for <c>x:Bind</c> expressions that combine view model states.</summary>
public static class BindingFunctions
{
    /// <summary>Returns whether both values are true; a control enabled by its page state and by the managed state uses it.</summary>
    public static bool And(bool first, bool second) => first && second;
}
