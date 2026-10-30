// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog.Core;
using Serilog.Events;

namespace Foundry.Bootstrap.Tests.Support;

/// <summary>Forwards every Serilog event to a test callback so a test can assert on the emitted events.</summary>
internal sealed class DelegatingSink(Action<LogEvent> emit) : ILogEventSink
{
    public void Emit(LogEvent logEvent) => emit(logEvent);
}
