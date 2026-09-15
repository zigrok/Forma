# Third-Party Notices

## Browser native text

The optional browser-native build uses FreeType 2.14.3 at
`0a0221a1347e2f1e07c395263540026e9a0aa7c7` under the FreeType Project License,
and HarfBuzz 14.4.0 at `36cb489cb02ce4b92099669ba9f9bea348eff93f` under its
Old MIT license and applicable component notices.

For the .NET workload's LLVM 19 setjmp ABI, the build also compiles the upstream
Emscripten `system/lib/compiler-rt/emscripten_setjmp.c` implementation at
`ceee49d2ecdab36a3feb85a684f8e5a453dde910` (3.1.68).
Copyright 2020 The Emscripten Authors. Emscripten offers this under the MIT
license or University of Illinois/NCSA Open Source License.

These sources are SHA-256-verified build downloads, not replacements for the
installed Emscripten toolchain. The build copies the complete upstream notices
to `artifacts/browser/native-text/licenses`; include those notices when
redistributing the linked browser platform.

The browser proof fetches full Japanese, Korean and Simplified-Chinese Noto Sans
CJK fonts at `notofonts/noto-cjk` revision
`f8d157532fbfaeda587e826d4cd5b21a49186f7c`. Their SIL Open Font License is
downloaded and copied beside the font content. Fonts are not compiled into the
native text library.

## XamlX

Forma's build-time XAML compiler uses a Forma-maintained fork of XamlX pinned in the
`external/XamlX` Git submodule. XamlX is distributed under the MIT License and remains a
compiler/tooling dependency; it is not linked into compiled Forma application output.

Copyright (c) 2019 Nikita Tsukanov

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## YamlDotNet

The build-only `Forma.AssemblyInspector` tool uses YamlDotNet 16.3.0 to parse generated Docfx
metadata for documentation coverage and control-reference validation. It is not linked into Forma
application or NuGet package output. YamlDotNet is distributed under the MIT License.

Source: https://github.com/aaubry/YamlDotNet at `ae480660f4fb26f3eb0b41c1d1fcf21c0e9d9e73`

Copyright (c) Antoine Aubry and contributors

## Godot Engine

Copyright (c) 2014-present Godot Engine contributors (see Godot's AUTHORS.md).
Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Forma's default control icons are copied from Godot's runtime-only
`scene/theme/icons` collection at revision `b4fb06cdb3db0c61db40c7b365bfa7adec3cb2ce`.
The complete import paths and source hashes are recorded in `assets/theme-icons/imports.json`; the
corresponding Godot license is distributed as `assets/theme-icons/LICENSE.Godot.txt`.

## Svg.Skia

The build-only `Forma.IconPipeline` tool and optional `Forma.Svg.MonoGame` / `Forma.Svg.FNA`
companions use Svg.Skia 5.2.0 and SkiaSharp 4.148.0. The tool rasterizes imported sources into
canonical PNG atlases; the companions provide bounded runtime SVG parsing and rasterization.
Core Forma packages do not depend on either library.

Source: https://github.com/wieslawsoltes/Svg.Skia

Copyright (c) 2019 Wiesław Šoltés

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## SkiaSharp

Svg.Skia and the Forma.Svg companions use SkiaSharp 4.148.0 as their rasterization runtime.

Source: https://github.com/mono/SkiaSharp

Copyright (c) 2015-2016 Xamarin, Inc.
Portions copyright (c) 2017-present Microsoft Corporation.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## ThorVG

The optional `Forma.Svg.ThorVG.MonoGame` and `Forma.Svg.ThorVG.FNA` companions use ThorVG 1.1.0,
pinned at commit `1b3aed2c188571f49ef4d87062b9f92ff1426c57`. Core and Skia packages do not
contain ThorVG. Source and build provenance are documented in `docs/thorvg-build-and-provenance.md`.

Source: https://github.com/thorvg/thorvg

Copyright (c) 2020 - 2026 ThorVG Project

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## Clipper2

Forma core packages include Clipper2 2.0.0 for deterministic polygon boolean operations and
triangulation. Clipper2 is distributed under the Boost Software License 1.0. Its complete license
is included in each core package at `licenses/geometry/Clipper2.LICENSE.txt`.

Copyright (c) 2010-2025 Angus Johnson

## ok_color

Copyright (c) 2021 Bjorn Ottosson

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## MonoGame

Forma references MonoGame as a separate dependency. The test-only
`tests/Forma.RenderTests/GraphicsDeviceTestFixtureBase.cs` is also a reduced adaptation of MonoGame's
graphics test fixture.

Microsoft Public License (Ms-PL)
MonoGame - Copyright (c) 2009-2026 MonoGame Foundation, Inc

All rights reserved.

This license governs use of the accompanying software. If you use the software,
you accept this license. If you do not accept the license, do not use the
software.

1. Definitions

The terms "reproduce," "reproduction," "derivative works," and "distribution"
have the same meaning here as under U.S. copyright law.

A "contribution" is the original software, or any additions or changes to the
software.

A "contributor" is any person that distributes its contribution under this
license.

"Licensed patents" are a contributor's patent claims that read directly on its
contribution.

2. Grant of Rights

(A) Copyright Grant- Subject to the terms of this license, including the
license conditions and limitations in section 3, each contributor grants you a
non-exclusive, worldwide, royalty-free copyright license to reproduce its
contribution, prepare derivative works of its contribution, and distribute its
contribution or any derivative works that you create.

(B) Patent Grant- Subject to the terms of this license, including the license
conditions and limitations in section 3, each contributor grants you a
non-exclusive, worldwide, royalty-free license under its licensed patents to
make, have made, use, sell, offer for sale, import, and/or otherwise dispose of
its contribution in the software or derivative works of the contribution in the
software.

3. Conditions and Limitations

(A) No Trademark License- This license does not grant you rights to use any
contributors' name, logo, or trademarks.

(B) If you bring a patent claim against any contributor over patents that you
claim are infringed by the software, your patent license from such contributor
to the software ends automatically.

(C) If you distribute any portion of the software, you must retain all
copyright, patent, trademark, and attribution notices that are present in the
software.

(D) If you distribute any portion of the software in source code form, you may
do so only under this license by including a complete copy of this license with
your distribution. If you distribute any portion of the software in compiled or
object code form, you may only do so under a license that complies with this
license.

(E) The software is licensed "as-is." You bear the risk of using it. The
contributors give no express warranties, guarantees or conditions. You may have
additional consumer rights under your local laws which this license cannot
change. To the extent permitted under your local laws, the contributors exclude
the implied warranties of merchantability, fitness for a particular purpose and
non-infringement.

## FNA.NET

Forma may reference FNA.NET as a separate dependency. FNA.NET is an opinionated fork of FNA and is
distributed under the Microsoft Public License reproduced in the MonoGame section above. Its
`FNA.NET.NativeAssets` dependency is distributed separately and includes FNA's platform-native
dependencies. See the restored packages for their complete license and attribution files.

FNA - Copyright 2009-2024 Ethan Lee and the MonoGame Team
FNA.NET - Copyright 2026 FNA-NET

## Inter

Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter)

The catalog UI font input is a TTF expansion of Godot's bundled `Inter_Regular.woff2`. The font and
generated XNB artifacts are licensed under the SIL Open Font License, Version 1.1. The complete
license is distributed at `tests/Assets/Fonts/LICENSE.Inter.txt` and beside the catalog runtime
assets.

## JetBrains Mono

Copyright 2020, The JetBrains Mono Project Authors
(https://github.com/JetBrains/JetBrainsMono)

The catalog code font input is a TTF expansion of Godot's bundled `JetBrainsMono_Regular.woff2`.
The font and generated XNB artifacts are licensed under the SIL Open Font License, Version 1.1. The
complete license is distributed at `tests/Assets/Fonts/LICENSE.JetBrainsMono.txt` and beside the
catalog runtime assets.

## Noto Sans Arabic

Copyright 2016 The Noto Project Authors (https://github.com/notofonts/arabic)

The dynamic-text test fixture is licensed under the SIL Open Font License, Version 1.1. The complete
license is distributed at `tests/Assets/Fonts/LICENSE.NotoSansArabic.txt`.

## Noto Multilingual Test Subsets

Copyright 2015-2026 The Noto Project Authors (https://github.com/notofonts)

Test-only subsets of Noto Sans Devanagari, Noto Sans Thai, Noto Sans Hebrew, Noto Sans SC, and Noto
Emoji are generated from `google/fonts` revision
`2796410152d4f9524b68ed46e69c1b60f8e0f7c3`. Source hashes and the deterministic subsetting command
are recorded in `scripts/generate-multilingual-font-subsets.sh`. The fonts are licensed under the
SIL Open Font License, Version 1.1; the complete license is distributed at
`tests/Assets/Fonts/LICENSE.NotoSubsets.txt`.

## FreeType

Forma dynamic text uses FreeType 2.13.2 through FreeTypeSharp 3.1.0. FreeTypeSharp is MIT
licensed. FreeType is available under the FreeType License or GPL-2.0; Forma uses the FreeType
License option. See https://github.com/ryancheung/FreeTypeSharp/blob/a628eb1028605703254c469d41a6d28c25442912/LICENSE
and https://freetype.org/license.html.

This software is based in part on the work of the FreeType Team.

## HarfBuzz

Forma dynamic text uses HarfBuzz 14.2.1 through HarfBuzzSharp 14.2.1.1. Both are MIT licensed.
See https://github.com/harfbuzz/harfbuzz/blob/14.2.1/COPYING and
https://licenses.nuget.org/MIT.

Copyright © 2010-2022 Google, Inc.
Copyright © 2015-2020 Ebrahim Byagowi
Copyright © 2019,2020 Facebook, Inc.
Copyright © 2012,2015 Mozilla Foundation
Copyright © 2011 Codethink Limited
Copyright © 2008,2010 Nokia Corporation and/or its subsidiary(-ies)
Copyright © 2009 Keith Stribley
Copyright © 2011 Martin Hosken and SIL International
Copyright © 2007 Chris Wilson
Copyright © 2005,2006,2020,2021,2022,2023 Behdad Esfahbod
Copyright © 2004,2007,2008,2009,2010,2013,2021,2022,2023 Red Hat, Inc.
Copyright © 1998-2005 David Turner and Werner Lemberg
Copyright © 2016 Igalia S.L.
Copyright © 2022 Matthias Clasen
Copyright © 2018,2021 Khaled Hosny
Copyright © 2018,2019,2020 Adobe, Inc
Copyright © 2013-2015 Alexei Podtelezhnikov

Permission is hereby granted, without written agreement and without license or royalty fees, to
use, copy, modify, and distribute this software and its documentation for any purpose, provided
that the above copyright notice and the following two paragraphs appear in all copies of this
software.

IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE TO ANY PARTY FOR DIRECT, INDIRECT, SPECIAL,
INCIDENTAL, OR CONSEQUENTIAL DAMAGES ARISING OUT OF THE USE OF THIS SOFTWARE AND ITS DOCUMENTATION,
EVEN IF THE COPYRIGHT HOLDER HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

THE COPYRIGHT HOLDER SPECIFICALLY DISCLAIMS ANY WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE. THE SOFTWARE PROVIDED
HEREUNDER IS ON AN "AS IS" BASIS, AND THE COPYRIGHT HOLDER HAS NO OBLIGATION TO PROVIDE MAINTENANCE,
SUPPORT, UPDATES, ENHANCEMENTS, OR MODIFICATIONS.

## Unicode Character Database

Forma's generated text-segmentation tables and conformance fixtures are derived from Unicode
Character Database 17.0.0 data files distributed under the Unicode License V3.

COPYRIGHT AND PERMISSION NOTICE

Copyright © 1991-2026 Unicode, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy of data files and any
associated documentation (the "Data Files") or software and any associated documentation (the
"Software") to deal in the Data Files or Software without restriction, including without
limitation the rights to use, copy, modify, merge, publish, distribute, and/or sell copies of the
Data Files or Software, and to permit persons to whom the Data Files or Software are furnished to
do so, provided that either (a) this copyright and permission notice appear with all copies of the
Data Files or Software, or (b) this copyright and permission notice appear in associated
Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT OF THIRD PARTY RIGHTS.

IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE BE LIABLE FOR ANY CLAIM,
OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES, OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF
USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder shall not be used in advertising
or otherwise to promote the sale, use or other dealings in these Data Files or Software without
prior written authorization of the copyright holder.