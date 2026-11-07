# Third-party notices

## Noise-C

- Project: https://github.com/rweather/noise-c
- Commit: `cfe25410979a87391bb9ac8d4d4bef64e9f268c6` (2023-12-07)
- Version: 0.2.x development tree (no tagged release in the tested tree)
- License: MIT

The POC compiles the reference implementation with its `ref` backend
(Curve25519, ChaCha20-Poly1305, AES-GCM, SHA-256/512, BLAKE2s/b) and the
OS random provider. The POC only exercises
`Noise_XX_25519_ChaChaPoly_BLAKE2s`; Curve448, NewHope and Ed25519 signing
are linked out via the small stubs in `stubs.c`. No Noise-C source file is
modified.

The compiled binaries (`noise.dll`, `libnoise.so` for arm64-v8a and x86_64)
are redistributed for the spike under the MIT license below.

```
MIT License

Copyright (C) 2016 Southern Storm Software, Pty Ltd.

Permission is hereby granted, free of charge, to any person obtaining a
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```
