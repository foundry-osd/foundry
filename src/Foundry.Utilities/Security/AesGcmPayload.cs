// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Security;

public sealed record AesGcmPayload(byte[] Nonce, byte[] Tag, byte[] Ciphertext);
