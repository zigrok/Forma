cmake_minimum_required(VERSION 3.24)
project(forma_browser_text LANGUAGES C CXX)
if(NOT EMSCRIPTEN)
  message(FATAL_ERROR "Use build-wasm.sh with the Emscripten toolchain from the pinned .NET workload.")
endif()
include(FetchContent)
# Match .NET's wasm-EH runtime; FreeType's setjmp must not use the JS-longjmp ABI.
add_compile_options(-fwasm-exceptions -sSUPPORT_LONGJMP=wasm)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(FT_DISABLE_ZLIB ON CACHE BOOL "" FORCE)
set(FT_DISABLE_BZIP2 ON CACHE BOOL "" FORCE)
set(FT_DISABLE_PNG ON CACHE BOOL "" FORCE)
set(FT_DISABLE_HARFBUZZ ON CACHE BOOL "" FORCE)
set(FT_DISABLE_BROTLI ON CACHE BOOL "" FORCE)
set(HB_BUILD_UTILS OFF CACHE BOOL "" FORCE)
set(HB_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(HB_BUILD_SUBSET OFF CACHE BOOL "" FORCE)
set(HB_BUILD_RASTER OFF CACHE BOOL "" FORCE)
set(HB_BUILD_VECTOR OFF CACHE BOOL "" FORCE)
set(HB_BUILD_GPU OFF CACHE BOOL "" FORCE)
set(HB_HAVE_FREETYPE OFF CACHE BOOL "" FORCE)
set(HB_HAVE_GLIB OFF CACHE BOOL "" FORCE)
set(HB_HAVE_ICU OFF CACHE BOOL "" FORCE)
set(HB_HAVE_CAIRO OFF CACHE BOOL "" FORCE)
set(HB_HAVE_CORETEXT OFF CACHE BOOL "" FORCE)
set(HB_HAVE_DIRECTWRITE OFF CACHE BOOL "" FORCE)

FetchContent_Declare(freetype
  URL https://codeload.github.com/freetype/freetype/tar.gz/0a0221a1347e2f1e07c395263540026e9a0aa7c7
  URL_HASH SHA256=11cd478953fc1d382f20a233b8e2aed6c31b47cbf0568c4bdb3334bcc1550698
  DOWNLOAD_NAME freetype-2.14.3.tar.gz
  DOWNLOAD_DIR "${FORMA_DOWNLOAD_DIR}"
)
FetchContent_Declare(harfbuzz
  URL https://codeload.github.com/harfbuzz/harfbuzz/tar.gz/36cb489cb02ce4b92099669ba9f9bea348eff93f
  URL_HASH SHA256=0afa12c8ef4bc4ffebd99e5d2a4a2c56dfe329c661feda08a9bc878b7352be89
  DOWNLOAD_NAME harfbuzz-14.4.0.tar.gz
  DOWNLOAD_DIR "${FORMA_DOWNLOAD_DIR}"
)
FetchContent_MakeAvailable(freetype harfbuzz)
add_subdirectory(.. bridge)

# The workload combines LLVM 19 with an older compiler-rt cache. Supply the
# upstream LLVM-19 setjmp ABI, not empty symbol stubs or a replacement toolchain.
set(sjlj_source "${FORMA_DOWNLOAD_DIR}/emscripten_setjmp.c")
if(NOT EXISTS "${sjlj_source}")
  file(DOWNLOAD
    https://raw.githubusercontent.com/emscripten-core/emscripten/ceee49d2ecdab36a3feb85a684f8e5a453dde910/system/lib/compiler-rt/emscripten_setjmp.c
    "${sjlj_source}"
    EXPECTED_HASH SHA256=99a7be1422119391b54b34260f623216f7a885f80c97b39568aaea5c88a267d3
    TLS_VERIFY ON)
endif()
file(SHA256 "${sjlj_source}" sjlj_hash)
if(NOT sjlj_hash STREQUAL "99a7be1422119391b54b34260f623216f7a885f80c97b39568aaea5c88a267d3")
  message(FATAL_ERROR "The pinned Emscripten setjmp implementation has an unexpected hash.")
endif()
target_sources(forma_dynamictext PRIVATE "${sjlj_source}")
target_include_directories(forma_dynamictext PRIVATE "${EMSCRIPTEN_ROOT_PATH}/system/lib/libc")
target_compile_definitions(forma_dynamictext PRIVATE __USING_WASM_SJLJ__)

set(sjlj_license "${FORMA_DOWNLOAD_DIR}/LICENSE.Emscripten")
if(NOT EXISTS "${sjlj_license}")
  file(DOWNLOAD
    https://raw.githubusercontent.com/emscripten-core/emscripten/ceee49d2ecdab36a3feb85a684f8e5a453dde910/LICENSE
    "${sjlj_license}"
    EXPECTED_HASH SHA256=620a78084fc7ca97c0b5dea9abf891f3ffcadfdbf305276f099c9c4e12fc1d86
    TLS_VERIFY ON)
endif()
file(SHA256 "${sjlj_license}" sjlj_license_hash)
if(NOT sjlj_license_hash STREQUAL "620a78084fc7ca97c0b5dea9abf891f3ffcadfdbf305276f099c9c4e12fc1d86")
  message(FATAL_ERROR "The pinned Emscripten license has an unexpected hash.")
endif()
configure_file("${sjlj_license}" "${CMAKE_BINARY_DIR}/licenses/Emscripten.txt" COPYONLY)
configure_file("${freetype_SOURCE_DIR}/docs/FTL.TXT" "${CMAKE_BINARY_DIR}/licenses/FreeType.txt" COPYONLY)
configure_file("${freetype_SOURCE_DIR}/LICENSE.TXT" "${CMAKE_BINARY_DIR}/licenses/FreeType-license-selection.txt" COPYONLY)
configure_file("${harfbuzz_SOURCE_DIR}/COPYING" "${CMAKE_BINARY_DIR}/licenses/HarfBuzz.txt" COPYONLY)
configure_file("${harfbuzz_SOURCE_DIR}/src/ms-use/COPYING" "${CMAKE_BINARY_DIR}/licenses/HarfBuzz-MS-USE.txt" COPYONLY)

# Keep all browser native archives together for host NativeFileReference items.
set_target_properties(freetype harfbuzz forma_dynamictext PROPERTIES
  ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")
