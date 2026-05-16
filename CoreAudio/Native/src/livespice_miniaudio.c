/*
 * Thin C shim over miniaudio so the managed CoreAudio binding can stay free of
 * the (large, version-sensitive) ma_device_config / ma_device struct layouts.
 *
 * Everything the C# side touches is either an opaque ls_* pointer or a small
 * POD return value.  Errors are surfaced as negative return codes; only
 * MA_SUCCESS (0) means "ok".
 *
 * Build:
 *   ./build.sh
 */

#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include <stdlib.h>
#include <string.h>

/* ---------- Context ----------------------------------------------------- */

ma_context* ls_context_create(void)
{
    ma_context* ctx = (ma_context*)malloc(sizeof(ma_context));
    if (ctx == NULL) return NULL;
    if (ma_context_init(NULL, 0, NULL, ctx) != MA_SUCCESS) {
        free(ctx);
        return NULL;
    }
    return ctx;
}

void ls_context_destroy(ma_context* ctx)
{
    if (ctx == NULL) return;
    ma_context_uninit(ctx);
    free(ctx);
}

/* ---------- Device enumeration ----------------------------------------- */
/*
 * ls_context_enumerate fills four out-parameters with pointers into miniaudio-
 * owned device-info arrays.  The arrays remain valid until the next call to
 * ls_context_enumerate or ls_context_destroy, matching the lifetime contract
 * of ma_context_get_devices.
 */
int ls_context_enumerate(
    ma_context* ctx,
    ma_device_info** playback, int* playback_count,
    ma_device_info** capture,  int* capture_count)
{
    ma_uint32 pc = 0, cc = 0;
    ma_result r = ma_context_get_devices(ctx, playback, &pc, capture, &cc);
    if (r != MA_SUCCESS) return (int)r;
    *playback_count = (int)pc;
    *capture_count  = (int)cc;
    return 0;
}

const char* ls_device_info_name(ma_device_info* info) { return info ? info->name : NULL; }
ma_device_id* ls_device_info_id(ma_device_info* info) { return info ? &info->id : NULL; }
size_t ls_device_info_size(void) { return sizeof(ma_device_info); }

/*
 * Populate channel counts for a given device-info entry by probing it with
 * ma_context_get_device_info.  miniaudio's minimum/maximum channel fields are
 * informational; we report the native channel count from the first native
 * data format if available, otherwise fall back to the maxChannels field.
 */
int ls_context_get_device_channels(
    ma_context* ctx,
    ma_device_type type,
    ma_device_id* id,
    int* channels)
{
    ma_device_info info;
    memset(&info, 0, sizeof(info));
    ma_result r = ma_context_get_device_info(ctx, type, id, &info);
    if (r != MA_SUCCESS) {
        *channels = 0;
        return (int)r;
    }
    int ch = 0;
    if (info.nativeDataFormatCount > 0) {
        for (ma_uint32 i = 0; i < info.nativeDataFormatCount; ++i) {
            if ((int)info.nativeDataFormats[i].channels > ch)
                ch = (int)info.nativeDataFormats[i].channels;
        }
    }
    if (ch == 0) ch = 2; /* sensible CoreAudio default */
    *channels = ch;
    return 0;
}

/* ---------- Device / stream ------------------------------------------- */

typedef void (*ls_data_callback)(
    void* userdata,
    const float* input,  int input_channels,
    float*       output, int output_channels,
    int frame_count);

typedef struct ls_device {
    ma_device        device;
    ls_data_callback cb;
    void*            userdata;
} ls_device;

static void ls_on_data(ma_device* device, void* output, const void* input, ma_uint32 frameCount)
{
    ls_device* d = (ls_device*)device->pUserData;
    if (d == NULL || d->cb == NULL) return;
    d->cb(
        d->userdata,
        (const float*)input,  (int)device->capture.channels,
        (float*)output,       (int)device->playback.channels,
        (int)frameCount);
}

/*
 * Create a duplex / capture-only / playback-only device.  Pass NULL for the
 * id of a side that should be disabled (and the corresponding channel count
 * will be ignored).
 */
ls_device* ls_device_create(
    ma_context*   ctx,
    ma_device_id* capture_id,  int capture_channels,
    ma_device_id* playback_id, int playback_channels,
    int sample_rate,
    int period_frames,
    ls_data_callback cb,
    void* userdata)
{
    if (ctx == NULL || cb == NULL) return NULL;

    ls_device* d = (ls_device*)calloc(1, sizeof(ls_device));
    if (d == NULL) return NULL;
    d->cb = cb;
    d->userdata = userdata;

    ma_device_type type;
    if (capture_id && playback_id)      type = ma_device_type_duplex;
    else if (capture_id)                type = ma_device_type_capture;
    else if (playback_id)               type = ma_device_type_playback;
    else { free(d); return NULL; }

    ma_device_config cfg = ma_device_config_init(type);
    cfg.sampleRate       = (ma_uint32)sample_rate;
    cfg.periodSizeInFrames = (ma_uint32)period_frames;
    cfg.dataCallback     = ls_on_data;
    cfg.pUserData        = d;

    if (capture_id) {
        cfg.capture.pDeviceID = capture_id;
        cfg.capture.format    = ma_format_f32;
        cfg.capture.channels  = (ma_uint32)capture_channels;
        cfg.capture.shareMode = ma_share_mode_shared;
    }
    if (playback_id) {
        cfg.playback.pDeviceID = playback_id;
        cfg.playback.format    = ma_format_f32;
        cfg.playback.channels  = (ma_uint32)playback_channels;
        cfg.playback.shareMode = ma_share_mode_shared;
    }

    if (ma_device_init(ctx, &cfg, &d->device) != MA_SUCCESS) {
        free(d);
        return NULL;
    }
    return d;
}

int  ls_device_start(ls_device* d) { return d ? (int)ma_device_start(&d->device) : -1; }
int  ls_device_stop (ls_device* d) { return d ? (int)ma_device_stop (&d->device) : -1; }

void ls_device_destroy(ls_device* d)
{
    if (d == NULL) return;
    ma_device_uninit(&d->device);
    free(d);
}

int ls_device_sample_rate(ls_device* d)     { return d ? (int)d->device.sampleRate          : 0; }
int ls_device_capture_channels(ls_device* d){ return d ? (int)d->device.capture.channels    : 0; }
int ls_device_playback_channels(ls_device* d){return d ? (int)d->device.playback.channels   : 0; }
