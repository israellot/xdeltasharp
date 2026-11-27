#include "xdelta3.h"

/* Use the CLI's level mapping with uncompressed sections and no checksum.
   xd3_encode_memory always adds an Adler-32 checksum in xdelta3 3.2.2. */
int benchmark_encode(const uint8_t *source, usize_t source_size,
                     const uint8_t *target, usize_t target_size,
                     uint8_t *output, usize_t capacity, usize_t *written,
                     int level) {
    xd3_config config = {0};
    xd3_stream stream = {0};
    xd3_source dictionary = {0};
    config.flags = level == 0 ? XD3_NOCOMPRESS : level << XD3_COMPLEVEL_SHIFT;
    config.smatch_cfg = level == 0 ? XD3_SMATCH_FASTEST : XD3_SMATCH_DEFAULT;
    config.winsize = target_size < XD3_DEFAULT_WINSIZE ? target_size : XD3_DEFAULT_WINSIZE;
    config.sprevsz = XD3_DEFAULT_SPREVSZ;
    config.iopt_size = XD3_DEFAULT_IOPT_SIZE;

    int status = xd3_config_stream(&stream, &config);
    if (status == 0 && source_size > 0) {
        dictionary.curblk = source;
        dictionary.blksize = source_size;
        dictionary.onblk = source_size;
        dictionary.max_winsize = source_size;
        status = xd3_set_source_and_size(&stream, &dictionary, source_size);
    }
    if (status == 0)
        status = xd3_encode_stream(&stream, target, target_size, output, written, capacity);
    xd3_free_stream(&stream);
    return status;
}

int benchmark_decode(const uint8_t *source, usize_t source_size,
                     const uint8_t *patch, usize_t patch_size,
                     uint8_t *output, usize_t capacity, usize_t *written) {
    return xd3_decode_memory(patch, patch_size, source, source_size, output, written, capacity, 0);
}
