#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
fonts="$root/artifacts/browser/fonts"
mkdir -p "$fonts"
base=https://raw.githubusercontent.com/notofonts/noto-cjk/f8d157532fbfaeda587e826d4cd5b21a49186f7c/Sans
fetch() {
  local name="$1" path="$2" hash="$3"
  if [[ ! -f "$fonts/$name" ]]; then
    curl --fail --location --silent --show-error "$base/$path" -o "$fonts/$name"
  fi
  printf '%s  %s\n' "$hash" "$fonts/$name" | shasum -a 256 --check
}
fetch NotoSansCJKjp-Regular.otf OTF/Japanese/NotoSansCJKjp-Regular.otf 68a3fc98800b2a27b371f2fb79991daf3633bd89309d4ffaa6946fd587f375b5
fetch NotoSansCJKkr-Regular.otf OTF/Korean/NotoSansCJKkr-Regular.otf 6bcb2a0703aa137e874fc2dffa85f6c21ba9a67fa329e81b8c801663af7e992a
fetch NotoSansCJKsc-Regular.otf OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf 2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b
fetch LICENSE.NotoCJK.txt LICENSE 6a73f9541c2de74158c0e7cf6b0a58ef774f5a780bf191f2d7ec9cc53efe2bf2
cp "$root/tests/Assets/Fonts/Inter_Regular.ttf" "$root/tests/Assets/Fonts/NotoSansArabic_Variable.ttf" \
  "$root/tests/Assets/Fonts/LICENSE.Inter.txt" "$root/tests/Assets/Fonts/LICENSE.NotoSansArabic.txt" "$fonts/"
