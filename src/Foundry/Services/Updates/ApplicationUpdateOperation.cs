// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Velopack;

namespace Foundry.Services.Updates;

internal sealed record ApplicationUpdateOperation(Guid Id, UpdateManager Manager, UpdateInfo Update, string FeedUrl, string Channel);
