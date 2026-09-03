// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.ViewModels;

public sealed record MachineNameComponentChoice(MachineNameComponentType Type, string DisplayName);

public sealed record MachineNameSeparatorChoice(MachineNameSeparator Value, string DisplayName);

public sealed record MachineNameCasingChoice(MachineNameCasing Value, string DisplayName);
