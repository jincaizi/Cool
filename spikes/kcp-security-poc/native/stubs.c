/* POC build glue: Noise-C algorithm entry points that are not used by this
   spike. The reference backend subset that is built provides Curve25519,
   ChaCha20-Poly1305, AES-GCM, SHA-256/512 and BLAKE2s/b. */
#include "internal.h"

NoiseDHState *noise_curve448_new(void) { return 0; }
NoiseDHState *noise_newhope_new(void) { return 0; }
NoiseSignState *noise_ed25519_new(void) { return 0; }
