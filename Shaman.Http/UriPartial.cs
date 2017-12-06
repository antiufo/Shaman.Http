#if CORECLR && !NETSTANDARD20

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace System
{
    public enum UriPartial
    {
        Scheme,
        Authority,
        Path,
        Query
    }
}
#endif