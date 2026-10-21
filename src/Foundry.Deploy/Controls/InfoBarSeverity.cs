// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Controls;

/// <summary>Says how important the message of an <see cref="InfoBar"/> is, as the WinUI 3 InfoBar does.</summary>
public enum InfoBarSeverity
{
    /// <summary>Neutral information, such as an operation in progress.</summary>
    Informational,

    /// <summary>An operation succeeded.</summary>
    Success,

    /// <summary>Something needs attention but does not block the technician.</summary>
    Warning,

    /// <summary>Something failed or blocks the technician.</summary>
    Error
}
