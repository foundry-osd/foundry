// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.NetworkCache;

/// <summary>
/// Binds boot media to the signed content index published on the network cache shares: the key that must have signed it and the
/// lowest sequence it may carry.
/// </summary>
/// <param name="PublicKey">The DER SubjectPublicKeyInfo of the trusted publisher key.</param>
/// <param name="MinimumSequence">The lowest index sequence Deploy accepts from this media.</param>
public sealed record NetworkCacheIndexBinding(byte[] PublicKey, long MinimumSequence);
