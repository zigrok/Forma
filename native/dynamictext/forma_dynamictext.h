// Copyright (c) 2026 Igor Hipólito Vieira
// SPDX-License-Identifier: MIT
#ifndef FORMA_DYNAMICTEXT_H
#define FORMA_DYNAMICTEXT_H
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct fdt_face fdt_face;
typedef struct {
    int32_t face_count, face_index, glyph_count, units_per_em;
    float ascender, descender, line_gap, line_height, underline_position, underline_thickness;
} fdt_face_info;
typedef struct { uint32_t tag; float value; } fdt_variation;
typedef struct { uint32_t tag, value; } fdt_feature;
typedef struct { float width, height, bearing_x, bearing_y, advance_x, advance_y; } fdt_metrics;
typedef struct {
    uint32_t glyph_id;
    int32_t width, height, pitch, bearing_x, bearing_y;
    float advance_x;
} fdt_bitmap;
typedef struct {
    uint32_t glyph_id, cluster;
    int32_t advance_x, advance_y, offset_x, offset_y;
} fdt_glyph;
typedef struct {
    const uint16_t *text;
    int32_t length;
    float size;
    int32_t direction;
    const char *locale, *script;
    const fdt_feature *features;
    int32_t feature_count;
    const fdt_variation *variations;
    int32_t variation_count;
} fdt_shape_request;
typedef struct { fdt_glyph *glyphs; int32_t count, direction; } fdt_shape_result;

/* Version 1 uses fixed-width fields; only opaque handles and byte pointers have pointer width.
   Results: 0 success; otherwise FontLoadErrorCode + 1. Pointers returned by raster are borrowed
   until the next face operation. Shapes must be released with fdt_shape_free. */
uint32_t fdt_abi_version(void);
int32_t fdt_face_create(const uint8_t *bytes, int32_t length, int32_t index, fdt_face **result, fdt_face_info *info);
void fdt_face_destroy(fdt_face *face);
const char *fdt_family_name(fdt_face *face);
const char *fdt_style_name(fdt_face *face);
uint32_t fdt_glyph_id(fdt_face *face, uint32_t scalar);
uint32_t fdt_next_char(fdt_face *face, uint32_t current, uint32_t *glyph);
uint32_t fdt_first_char(fdt_face *face, uint32_t *glyph);
int32_t fdt_glyph_metrics(fdt_face *face, uint32_t glyph, float size, const fdt_variation *variations, int32_t count, fdt_metrics *result);
int32_t fdt_rasterize(fdt_face *face, uint32_t glyph, float size, float display_scale, int32_t hinting,
                      const fdt_variation *variations, int32_t count, fdt_bitmap *result, const uint8_t **pixels);
int32_t fdt_shape(fdt_face *face, const fdt_shape_request *request, fdt_shape_result *result);
void fdt_shape_free(fdt_glyph *glyphs);

#ifdef __cplusplus
}
#endif
#endif
