# Vendored AccessKit C bindings

`libaccesskit.dylib` and `accesskit.h` come from the
[accesskit-c](https://github.com/AccessKit/accesskit-c) **0.23.0** release zip, unmodified except
that the two thin macOS dylibs were combined:

```sh
curl -L -o accesskit-c.zip \
  https://github.com/AccessKit/accesskit-c/releases/download/0.23.0/accesskit-c-0.23.0.zip
unzip accesskit-c.zip
lipo -create \
  accesskit-c-0.23.0/lib/macos/arm64/shared/libaccesskit.dylib \
  accesskit-c-0.23.0/lib/macos/x86_64/shared/libaccesskit.dylib \
  -output macos/libaccesskit.dylib
cp accesskit-c-0.23.0/include/accesskit.h .
cp accesskit-c-0.23.0/LICENSE-MIT accesskit-c-0.23.0/LICENSE-APACHE \
   accesskit-c-0.23.0/LICENSE.chromium .
```

The release ships **thin** binaries per architecture, not a universal one, so the `lipo` step is
required — without it, one of the two architectures silently has no accessibility.

`accesskit.h` is not compiled against; it is kept because `AccessKitEnums.Generated.cs` is generated
from it and because the P/Invoke signatures have to be checkable against something. Regenerate the
enums when this is updated: the values are ordinals in a C enum, so inserting a role upstream
renumbers everything after it.

## Licensing

MIT OR Apache-2.0 (`LICENSE-MIT`, `LICENSE-APACHE`). Portions derive from Chromium under its
BSD-style licence (`LICENSE.chromium`). All three are redistributed here because the binary is.

## Signing

The prebuilt binaries are adhoc-signed with no team identifier. An application that ships this
inside a notarized bundle has to re-sign it with its own identity.
