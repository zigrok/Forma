// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
#include "forma_dynamictext.h"
#include <ft2build.h>
#include FT_FREETYPE_H
#include FT_MULTIPLE_MASTERS_H
#include FT_SYNTHESIS_H
#include <hb.h>
#include <hb-ot.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>
#include <limits.h>

_Static_assert(sizeof(fdt_face_info) == 40, "fdt_face_info ABI");
_Static_assert(sizeof(fdt_variation) == 8, "fdt_variation ABI");
_Static_assert(sizeof(fdt_feature) == 8, "fdt_feature ABI");
_Static_assert(sizeof(fdt_metrics) == 24, "fdt_metrics ABI");
_Static_assert(sizeof(fdt_bitmap) == 28, "fdt_bitmap ABI");
_Static_assert(sizeof(fdt_glyph) == 24, "fdt_glyph ABI");
_Static_assert(sizeof(fdt_shape_request) == (sizeof(void *) == 8 ? 72 : 40), "fdt_shape_request ABI");
_Static_assert(sizeof(fdt_shape_result) == (sizeof(void *) == 8 ? 16 : 12), "fdt_shape_result ABI");

enum { FDT_INVALID = 1, FDT_SOURCE_LIMIT = 2, FDT_FACE_INDEX = 3, FDT_UNSUPPORTED = 4,
       FDT_NATIVE = 5, FDT_RASTER_LIMIT = 6 };
struct fdt_face { uint8_t *bytes; FT_Library library; FT_Face ft; hb_face_t *hb; };

uint32_t fdt_abi_version(void) { return 1; }

void fdt_face_destroy(fdt_face *face)
{
    if (!face) return;
    if (face->hb) hb_face_destroy(face->hb);
    if (face->ft) FT_Done_Face(face->ft);
    if (face->library) FT_Done_FreeType(face->library);
    free(face->bytes);
    free(face);
}

int32_t fdt_face_create(const uint8_t *bytes, int32_t length, int32_t index, fdt_face **result, fdt_face_info *info)
{
    if (!result || !info) return FDT_INVALID;
    *result = NULL;
    if (!bytes || length <= 0) return FDT_INVALID;
    if (length > 64 * 1024 * 1024) return FDT_SOURCE_LIMIT;
    if (index < 0 || index >= 256) return FDT_FACE_INDEX;
    fdt_face *face = calloc(1, sizeof(*face));
    if (!face) return FDT_NATIVE;
    face->bytes = malloc((size_t)length);
    if (!face->bytes) { fdt_face_destroy(face); return FDT_NATIVE; }
    memcpy(face->bytes, bytes, (size_t)length);
    if (FT_Init_FreeType(&face->library)) { fdt_face_destroy(face); return FDT_NATIVE; }
    if (FT_New_Memory_Face(face->library, face->bytes, length, index, &face->ft)) {
        fdt_face_destroy(face); return FDT_INVALID;
    }
    FT_Face ft = face->ft;
    if (ft->num_faces > 256 || ft->num_glyphs > INT32_MAX || !FT_IS_SCALABLE(ft) ||
        !ft->units_per_EM || FT_Select_Charmap(ft, FT_ENCODING_UNICODE)) {
        fdt_face_destroy(face); return FDT_UNSUPPORTED;
    }
    hb_blob_t *blob = hb_blob_create((const char *)face->bytes, (unsigned)length, HB_MEMORY_MODE_READONLY, NULL, NULL);
    face->hb = hb_face_create(blob, (unsigned)index);
    hb_blob_destroy(blob);
    if (!hb_face_get_glyph_count(face->hb)) { fdt_face_destroy(face); return FDT_INVALID; }
    *info = (fdt_face_info) {
        (int32_t)ft->num_faces, (int32_t)ft->face_index, (int32_t)ft->num_glyphs, ft->units_per_EM,
        ft->ascender, ft->descender, ft->height - (ft->ascender - ft->descender), ft->height,
        ft->underline_position, ft->underline_thickness
    };
    *result = face;
    return 0;
}

const char *fdt_family_name(fdt_face *face) { return face->ft->family_name; }
const char *fdt_style_name(fdt_face *face) { return face->ft->style_name; }
uint32_t fdt_glyph_id(fdt_face *face, uint32_t scalar) { return FT_Get_Char_Index(face->ft, scalar); }
uint32_t fdt_first_char(fdt_face *face, uint32_t *glyph)
{
    FT_UInt id;
    FT_ULong scalar = FT_Get_First_Char(face->ft, &id);
    *glyph = id;
    return (uint32_t)scalar;
}
uint32_t fdt_next_char(fdt_face *face, uint32_t current, uint32_t *glyph)
{
    FT_UInt id;
    FT_ULong scalar = FT_Get_Next_Char(face->ft, current, &id);
    *glyph = id;
    return (uint32_t)scalar;
}

static int32_t apply_variations(fdt_face *face, const fdt_variation *variations, int32_t count)
{
    if (count < 0 || count > 64 || (count && !variations)) return FDT_INVALID;
    if (!FT_HAS_MULTIPLE_MASTERS(face->ft)) return 0;
    FT_MM_Var *axes = NULL;
    if (FT_Get_MM_Var(face->ft, &axes)) return FDT_NATIVE;
    if (axes->num_axis > 64) { FT_Done_MM_Var(face->library, axes); return FDT_INVALID; }
    FT_Fixed values[64];
    for (FT_UInt i = 0; i < axes->num_axis; i++) {
        values[i] = axes->axis[i].def;
        for (int32_t j = 0; j < count; j++) {
            if (variations[j].tag != axes->axis[i].tag) continue;
            double value = round((double)variations[j].value * 65536.0);
            if (!isfinite(value) || value < axes->axis[i].minimum || value > axes->axis[i].maximum) {
                FT_Done_MM_Var(face->library, axes); return FDT_INVALID;
            }
            values[i] = (FT_Fixed)value;
            break;
        }
    }
    FT_Error error = FT_Set_Var_Design_Coordinates(face->ft, axes->num_axis, values);
    FT_Done_MM_Var(face->library, axes);
    return error ? FDT_NATIVE : 0;
}

static int32_t load_glyph(fdt_face *face, uint32_t *glyph, FT_Int32 flags)
{
    if (*glyph >= (uint32_t)face->ft->num_glyphs) return FDT_INVALID;
    if (!FT_Load_Glyph(face->ft, *glyph, flags)) return 0;
    if (!*glyph || FT_Load_Glyph(face->ft, 0, flags)) return FDT_NATIVE;
    *glyph = 0;
    return 0;
}

int32_t fdt_glyph_metrics(fdt_face *face, uint32_t glyph, float size, const fdt_variation *variations, int32_t count, fdt_metrics *result)
{
    if (!face || !result || !isfinite(size) || size <= 0) return FDT_INVALID;
    int32_t error = apply_variations(face, variations, count);
    if (!error) error = load_glyph(face, &glyph, FT_LOAD_NO_SCALE | FT_LOAD_NO_HINTING | FT_LOAD_NO_BITMAP);
    if (error) return error;
    FT_Glyph_Metrics m = face->ft->glyph->metrics;
    float scale = size / face->ft->units_per_EM;
    *result = (fdt_metrics){m.width * scale, m.height * scale, m.horiBearingX * scale,
                           m.horiBearingY * scale, m.horiAdvance * scale, m.vertAdvance * scale};
    return 0;
}

int32_t fdt_rasterize(fdt_face *face, uint32_t glyph, float size, float display_scale, int32_t options,
                      const fdt_variation *variations, int32_t count, fdt_bitmap *result, const uint8_t **pixels)
{
    /* Low byte is the hinting mode; bits 8-9 request synthetic bold/oblique, keeping the ABI stable. */
    int32_t hinting = options & 0xff, synthesis = options >> 8;
    if (!face || !result || !pixels || !isfinite(size) || size <= 0 ||
        !isfinite(display_scale) || display_scale <= 0 || hinting < 0 || hinting > 3 ||
        synthesis < 0 || synthesis > 3) return FDT_INVALID;
    *pixels = NULL;
    float physical_size = size * display_scale;
    if (!isfinite(physical_size) || physical_size > 4096) return FDT_RASTER_LIMIT;
    int32_t error = apply_variations(face, variations, count);
    if (error) return error;
    if (FT_Set_Char_Size(face->ft, 0, (FT_F26Dot6)lroundf(physical_size * 64), 72, 72)) return FDT_NATIVE;
    FT_Int32 flags = FT_LOAD_DEFAULT;
    if (hinting == 1) flags |= FT_LOAD_NO_HINTING;
    else if (hinting == 2) flags |= FT_LOAD_FORCE_AUTOHINT;
    else if (hinting == 3) flags |= FT_LOAD_TARGET_LIGHT;
    error = load_glyph(face, &glyph, flags);
    if (error) return error;
    FT_GlyphSlot slot = face->ft->glyph;
    if (synthesis & 1) FT_GlyphSlot_Embolden(slot);
    if (synthesis & 2) FT_GlyphSlot_Oblique(slot);
    if (FT_Render_Glyph(slot, FT_RENDER_MODE_NORMAL)) return FDT_NATIVE;
    FT_Bitmap b = slot->bitmap;
    if (b.width > 4096 || b.rows > 4096 || (uint64_t)b.width * b.rows > 16 * 1024 * 1024) return FDT_RASTER_LIMIT;
    if (b.width && b.rows) {
        if (!b.buffer || b.pitch == INT_MIN || (unsigned)abs(b.pitch) < b.width) return FDT_INVALID;
        if (b.pixel_mode != FT_PIXEL_MODE_GRAY || b.num_grays != 256) return FDT_UNSUPPORTED;
    }
    *result = (fdt_bitmap){glyph, (int32_t)b.width, (int32_t)b.rows, b.pitch,
                          slot->bitmap_left, slot->bitmap_top, slot->advance.x / 64.0f / display_scale};
    *pixels = b.buffer;
    return 0;
}

int32_t fdt_shape(fdt_face *face, const fdt_shape_request *request, fdt_shape_result *result)
{
    if (!request || !result) return FDT_INVALID;
    const uint16_t *text = request->text;
    int32_t length = request->length, direction = request->direction;
    float size = request->size;
    const char *locale = request->locale, *script = request->script;
    const fdt_feature *features = request->features;
    const fdt_variation *variations = request->variations;
    int32_t feature_count = request->feature_count, variation_count = request->variation_count;
    fdt_glyph **glyphs = &result->glyphs;
    int32_t *count = &result->count, *resolved_direction = &result->direction;
    if (!face || !glyphs || !count || !resolved_direction || length < 0 || length > 1000000 ||
        (length && !text) || !isfinite(size) || size <= 0 || size * 64.0 >= INT32_MAX ||
        feature_count < 0 || feature_count > 65536 || (feature_count && !features) ||
        variation_count < 0 || variation_count > 64 || (variation_count && !variations) ||
        direction < 0 || direction > 2) return FDT_INVALID;
    *glyphs = NULL;
    *count = 0;
    hb_font_t *font = hb_font_create(face->hb);
    hb_ot_font_set_funcs(font);
    hb_variation_t vars[64];
    for (int32_t i = 0; i < variation_count; i++) {
        if (!isfinite(variations[i].value)) { hb_font_destroy(font); return FDT_INVALID; }
        vars[i] = (hb_variation_t){variations[i].tag, variations[i].value};
    }
    hb_font_set_variations(font, vars, (unsigned)variation_count);
    hb_font_set_scale(font, (int)lroundf(size * 64), (int)lroundf(size * 64));
    hb_buffer_t *buffer = hb_buffer_create();
    hb_buffer_set_cluster_level(buffer, HB_BUFFER_CLUSTER_LEVEL_MONOTONE_GRAPHEMES);
    hb_buffer_add_utf16(buffer, text, length, 0, length);
    if (direction) hb_buffer_set_direction(buffer, direction == 2 ? HB_DIRECTION_RTL : HB_DIRECTION_LTR);
    if (locale && *locale) hb_buffer_set_language(buffer, hb_language_from_string(locale, -1));
    if (script && *script) hb_buffer_set_script(buffer, hb_script_from_string(script, -1));
    hb_buffer_guess_segment_properties(buffer);
    hb_feature_t *feats = feature_count ? malloc((size_t)feature_count * sizeof(*feats)) : NULL;
    if (feature_count && !feats) { hb_buffer_destroy(buffer); hb_font_destroy(font); return FDT_NATIVE; }
    for (int32_t i = 0; i < feature_count; i++)
        feats[i] = (hb_feature_t){features[i].tag, features[i].value, 0, (unsigned)-1};
    hb_shape(font, buffer, feats, (unsigned)feature_count);
    free(feats);
    unsigned n = hb_buffer_get_length(buffer);
    int32_t error = 0;
    if (!hb_buffer_allocation_successful(buffer)) error = FDT_NATIVE;
    else if (n > 1000000) error = FDT_INVALID;
    else if (n) {
        *glyphs = malloc(n * sizeof(**glyphs));
        if (!*glyphs) error = FDT_NATIVE;
        else {
            const hb_glyph_info_t *infos = hb_buffer_get_glyph_infos(buffer, NULL);
            const hb_glyph_position_t *positions = hb_buffer_get_glyph_positions(buffer, NULL);
            for (unsigned i = 0; i < n; i++)
                (*glyphs)[i] = (fdt_glyph){infos[i].codepoint, infos[i].cluster, positions[i].x_advance,
                    positions[i].y_advance, positions[i].x_offset, positions[i].y_offset};
        }
    }
    *count = error ? 0 : (int32_t)n;
    *resolved_direction = hb_buffer_get_direction(buffer) == HB_DIRECTION_RTL ? 2 : 1;
    hb_buffer_destroy(buffer);
    hb_font_destroy(font);
    return error;
}

void fdt_shape_free(fdt_glyph *glyphs) { free(glyphs); }
