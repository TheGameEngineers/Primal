// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
#include "ToolsCommon.h"
#include "Content/ContentToEngine.h"
#include "Utilities/IOStream.h"
#include <DirectXTex.h>
#include <dxgi1_6.h>

using namespace DirectX;
using namespace Microsoft::WRL;

namespace primal::tools {

bool is_normal_map(const Image *const image);

namespace {

struct import_error {
    enum error_code : u32 {
        succeeded = 0,
        unknown,
        compress,
        decompress,
        load,
        mipmap_generation,
        max_size_exceeded,
        size_mismatch,
        format_mismatch,
        file_not_found,
        need_six_images,
    };
};

struct texture_dimension {
    enum dimension : u32 {
        texture_1d,
        texture_2d,
        texture_3d,
        texture_cube
    };
};

struct texture_import_settings
{
    char*   sources;        // string of one or more file paths separated by semi-colons ';'
    u32     source_count;   // number of file paths
    u32     dimension;
    u32     mip_levels;
    f32     alpha_threshold;
    u32     prefer_bc7;
    u32     output_format;
    u32     compress;
};

struct texture_info
{
    u32 width;
    u32 height;
    u32 array_size;
    u32 mip_levels;
    u32 format;
    u32 import_error;
    u32 flags;
};

struct texture_data
{
    constexpr static u32    max_mips{ 14 }; // we support up to 8k textures.
    u8*                     subresource_data;
    u32                     subresource_size;
    u8*                     icon;
    u32                     icon_size;
    texture_info            info;
    texture_import_settings import_settings;
};

struct d3d11_device
{
    ComPtr<ID3D11Device>    device;
    std::mutex              hw_compression_mutex;
};

std::mutex                  device_creation_mutex;
utl::vector<d3d11_device>   d3d11_devices;

HMODULE dxgi_module{ nullptr };
HMODULE d3d11_module{ nullptr };

utl::vector<ComPtr<IDXGIAdapter>>
get_adapters_by_performance()
{
    if (!dxgi_module)
    {
        dxgi_module = LoadLibrary(L"dxgi.dll");
        if (!dxgi_module) return {};
    }

    using PFN_CreateDXGIFactory1 = HRESULT(WINAPI*)(REFIID, void**);
    const PFN_CreateDXGIFactory1 create_dxgi_factory1{ (PFN_CreateDXGIFactory1)((void*)GetProcAddress(dxgi_module, "CreateDXGIFactory1")) };
    if (!create_dxgi_factory1) return {};


    ComPtr<IDXGIFactory7> factory;
    utl::vector<ComPtr<IDXGIAdapter>> adapters;

    if (SUCCEEDED(create_dxgi_factory1(IID_PPV_ARGS(factory.GetAddressOf()))))
    {
        constexpr u32 warp_id{ 0x1414 };

        ComPtr<IDXGIAdapter> adapter;
        for (u32 i{ 0 }; factory->EnumAdapterByGpuPreference(i, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, IID_PPV_ARGS(&adapter)) != DXGI_ERROR_NOT_FOUND; ++i)
        {
            if (!adapter) continue;

            DXGI_ADAPTER_DESC desc;
            adapter->GetDesc(&desc);

            if (desc.VendorId != warp_id) adapters.emplace_back(adapter);

            adapter.Reset();
        }
    }

    return adapters;
}

void
create_device()
{
    if (d3d11_devices.size()) return;

    if (!d3d11_module)
    {
        d3d11_module = LoadLibrary(L"d3d11.dll");
        if (!d3d11_module) return;
    }

    const PFN_D3D11_CREATE_DEVICE d3d11_create_device{ (PFN_D3D11_CREATE_DEVICE)((void*)GetProcAddress(d3d11_module, "D3D11CreateDevice")) };
    if (!d3d11_create_device) return;

    u32 create_device_flags{ 0 };
#ifdef _DEBUG
    create_device_flags |= D3D11_CREATE_DEVICE_DEBUG;
#endif

    utl::vector<ComPtr<IDXGIAdapter>> adapters{ get_adapters_by_performance() };
    utl::vector<ComPtr<ID3D11Device>> devices(adapters.size(), nullptr);
    constexpr D3D_FEATURE_LEVEL feature_levels[]{ D3D_FEATURE_LEVEL_11_0 };

    for (u32 i{ 0 }; i < adapters.size(); ++i)
    {
        ID3D11Device** device{ &devices[i] };
        D3D_FEATURE_LEVEL feature_level;
        [[maybe_unused]]
        HRESULT hr{ d3d11_create_device(adapters[i].Get(), adapters[i] ? D3D_DRIVER_TYPE_UNKNOWN : D3D_DRIVER_TYPE_HARDWARE,
                                       nullptr, create_device_flags, feature_levels, _countof(feature_levels),
                                       D3D11_SDK_VERSION, device, &feature_level, nullptr) };
        assert(SUCCEEDED(hr));
    }

    for (u32 i{ 0 }; i < devices.size(); ++i)
    {
        // NOTE: we check for valid devices since device creation can fail for adapters that don't support
        //       the requested feature level (D3D_FEATURE_LEVEL_11_0).
        if (devices[i])
        {
            d3d11_devices.emplace_back();
            d3d11_devices.back().device = devices[i];
        }
    }
}

constexpr void
set_or_clear_flag(u32& flags, u32 flag, bool set)
{
    if (set) flags |= flag; else flags &= ~flag;
}

constexpr u32
get_max_mip_count(u32 width, u32 height, u32 depth)
{
    u32 mip_levels{ 1 };
    while (width > 1 || height > 1 || depth > 1)
    {
        width >>= 1;
        height >>= 1;
        depth >>= 1;

        ++mip_levels;
    }

    return mip_levels;
}

void
texture_info_from_metadata(const TexMetadata& metadata, texture_info& info)
{
    using namespace primal::content;
    const DXGI_FORMAT format{ metadata.format };
    info.format = format;
    info.width = (u32)metadata.width;
    info.height = (u32)metadata.height;
    info.array_size = metadata.IsVolumemap() ? (u32)metadata.depth : (u32)metadata.arraySize;
    info.mip_levels = (u32)metadata.mipLevels;
    set_or_clear_flag(info.flags, texture_flags::has_alpha, HasAlpha(format));
    set_or_clear_flag(info.flags, texture_flags::is_hdr, format == DXGI_FORMAT_BC6H_UF16 || format == DXGI_FORMAT_BC6H_SF16);
    set_or_clear_flag(info.flags, texture_flags::is_premultiplied_alpha, metadata.IsPMAlpha());
    set_or_clear_flag(info.flags, texture_flags::is_cube_map, metadata.IsCubemap());
    set_or_clear_flag(info.flags, texture_flags::is_volume_map, metadata.IsVolumemap());
    set_or_clear_flag(info.flags, texture_flags::is_srgb, IsSRGB(format));
}

void
copy_subresources(const ScratchImage& scratch, texture_data *const data)
{
    const TexMetadata& metadata{ scratch.GetMetadata() };
    const Image *const images{ scratch.GetImages() };
    const u32 image_count{ (u32)scratch.GetImageCount() };
    assert(images && metadata.mipLevels && metadata.mipLevels <= texture_data::max_mips);

    u64 subresource_size{ 0 };

    for (u32 i{ 0 }; i < image_count; ++i)
    {
        // 4 x u32 for width, height, rowPitch and slicePitch
        subresource_size += sizeof(u32) * 4 + images[i].slicePitch;
    }

    if (subresource_size > ~(u32)0)
    {
        // Support up to 4GB per resource.
        data->info.import_error = import_error::max_size_exceeded;
        return;
    }

    data->subresource_size = (u32)subresource_size;
    data->subresource_data = (u8 *const)CoTaskMemRealloc(data->subresource_data, subresource_size);
    assert(data->subresource_data);

    utl::blob_stream_writer blob{ data->subresource_data, data->subresource_size };

    for (u32 i{ 0 }; i < image_count; ++i)
    {
        const Image& image{ images[i] };
        blob.write((u32)image.width);
        blob.write((u32)image.height);
        blob.write((u32)image.rowPitch);
        blob.write((u32)image.slicePitch);
        blob.write(image.pixels, image.slicePitch);
    }
}

[[nodiscard]] utl::vector<Image>
subresource_data_to_images(texture_data *const data)
{
    assert(data && data->subresource_data && data->subresource_size);
    assert(data->info.mip_levels && data->info.mip_levels <= texture_data::max_mips);
    assert(data->info.array_size);

    const texture_info& info{ data->info };
    u32 image_count{ info.array_size };

    if (info.flags & content::texture_flags::is_volume_map)
    {
        u32 depth_per_mip_level{ info.array_size };
        for (u32 i{ 1 }; i < info.mip_levels; ++i)
        {
            depth_per_mip_level = std::max(depth_per_mip_level >> 1, (u32)1);
            image_count += depth_per_mip_level;
        }
    }
    else
    {
        image_count *= info.mip_levels;
    }

    utl::blob_stream_reader blob{ data->subresource_data };
    utl::vector<Image> images(image_count);

    for (u32 i{ 0 }; i < image_count; ++i)
    {
        Image image{};
        image.width = blob.read<u32>();
        image.height = blob.read<u32>();
        image.format = (DXGI_FORMAT)info.format;
        image.rowPitch = blob.read<u32>();
        image.slicePitch = blob.read<u32>();
        image.pixels = (u8*)blob.position();

        blob.skip(image.slicePitch);

        images[i] = image;
    }

    return images;
}

void
copy_icon(const Image& bc_image, texture_data *const data)
{
    ScratchImage scratch;
    if (FAILED(Decompress(bc_image, DXGI_FORMAT_UNKNOWN, scratch)))
    {
        return;
    }

    assert(scratch.GetImages());
    const Image& image{ scratch.GetImages()[0] };

    // 4 x u32 for width, height, rowPitch and slicePitch
    data->icon_size = (u32)(sizeof(u32) * 4 + image.slicePitch);
    data->icon = (u8 *const)CoTaskMemRealloc(data->icon, data->icon_size);
    assert(data->icon);

    utl::blob_stream_writer blob{ data->icon, data->icon_size };
    blob.write((u32)image.width);
    blob.write((u32)image.height);
    blob.write((u32)image.rowPitch);
    blob.write((u32)image.slicePitch);
    blob.write(image.pixels, image.slicePitch);
}

[[nodiscard]] ScratchImage
load_from_file(texture_data *const data, const char* file_name)
{
    using namespace primal::content;
    assert(file_exists(file_name));

    if (!file_exists(file_name))
    {
        data->info.import_error = import_error::file_not_found;
        return {};
    }

    data->info.import_error = import_error::load;

    WIC_FLAGS wic_flags{ WIC_FLAGS_NONE };
    TGA_FLAGS tga_flags{ TGA_FLAGS_NONE };

    if (data->import_settings.output_format == DXGI_FORMAT_BC4_UNORM ||
        data->import_settings.output_format == DXGI_FORMAT_BC5_UNORM)
    {
        wic_flags |= WIC_FLAGS_IGNORE_SRGB;
        tga_flags |= TGA_FLAGS_IGNORE_SRGB;
    }

    const std::wstring wfile{ to_wstring(file_name) };
    const wchar_t *const file{ wfile.c_str() };
    ScratchImage scratch;

    // Try one of WIC formats first (e.g. BMP, JPEG, PNG, etc.).
    wic_flags |= WIC_FLAGS_FORCE_RGB;
    HRESULT hr{ LoadFromWICFile(file, wic_flags, nullptr, scratch) };

    // It wasn't a WIC format. Try TGA.
    if (FAILED(hr))
    {
        hr = LoadFromTGAFile(file, tga_flags, nullptr, scratch);
    }

    // It wasn't a TGA either. Try HDR.
    if (FAILED(hr))
    {
        hr = LoadFromHDRFile(file, nullptr, scratch);
        if (SUCCEEDED(hr)) data->info.flags |= texture_flags::is_hdr;
    }

    // It wasn't HDR. Try DDS.
    if (FAILED(hr))
    {
        hr = LoadFromDDSFile(file, DDS_FLAGS_FORCE_RGB, nullptr, scratch);
        if (SUCCEEDED(hr))
        {
            data->info.import_error = import_error::decompress;
            ScratchImage mip_scratch;
            hr = Decompress(scratch.GetImages(), scratch.GetImageCount(), scratch.GetMetadata(),
                            DXGI_FORMAT_UNKNOWN, mip_scratch);

            if (SUCCEEDED(hr))
            {
                scratch = std::move(mip_scratch);
            }
        }
    }

    if (SUCCEEDED(hr))
    {
        data->info.import_error = import_error::succeeded;
    }

    return scratch;
}

[[nodiscard]] ScratchImage
initialize_from_images(texture_data *const data, const utl::vector<Image>& images)
{
    assert(data);
    const texture_import_settings& settings{ data->import_settings };

    ScratchImage scratch;
    HRESULT hr{ S_OK };
    const u32 array_size{ (u32)images.size() };

    { // Scope for working scratch
        ScratchImage working_scratch{};

        if (settings.dimension == texture_dimension::texture_1d ||
            settings.dimension == texture_dimension::texture_2d)
        {
            const bool allow_1d{ settings.dimension == texture_dimension::texture_1d };
            assert(array_size >= 1 && images.size() >= 1);
            hr = working_scratch.InitializeArrayFromImages(images.data(), images.size(), allow_1d);
        }
        else if (settings.dimension == texture_dimension::texture_cube)
        {
            if (array_size % 6)
            {
                data->info.import_error = import_error::need_six_images;
                return {};
            }

            hr = working_scratch.InitializeCubeFromImages(images.data(), images.size());
        }
        else
        {
            assert(settings.dimension == texture_dimension::texture_3d);
            hr = working_scratch.Initialize3DFromImages(images.data(), images.size());
        }

        if (FAILED(hr))
        {
            data->info.import_error = import_error::unknown;
            return {};
        }

        scratch = std::move(working_scratch);
    }

    if (settings.mip_levels != 1)
    {
        ScratchImage mip_scratch;
        const TexMetadata& metadata{ scratch.GetMetadata() };
        u32 mip_levels{ math::clamp(settings.mip_levels, (u32)0, get_max_mip_count((u32)metadata.width, (u32)metadata.height, (u32)metadata.depth)) };

        if (settings.dimension != texture_dimension::texture_3d)
        {
            hr = GenerateMipMaps(scratch.GetImages(), scratch.GetImageCount(), scratch.GetMetadata(),
                                 TEX_FILTER_DEFAULT, mip_levels, mip_scratch);
        }
        else
        {
            hr = GenerateMipMaps3D(scratch.GetImages(), scratch.GetImageCount(), scratch.GetMetadata(),
                                   TEX_FILTER_DEFAULT, mip_levels, mip_scratch);
        }

        if (FAILED(hr))
        {
            data->info.import_error = import_error::mipmap_generation;
            return {};
        }

        scratch = std::move(mip_scratch);
    }

    return scratch;
}

DXGI_FORMAT
determine_output_format(texture_data *const data, ScratchImage& scratch, const Image *const image)
{
    assert(data && data->import_settings.compress);
    using namespace primal::content;
    const DXGI_FORMAT image_format{ image->format };
    DXGI_FORMAT output_format{ (DXGI_FORMAT)data->import_settings.output_format };

    // Determine the best block compressed format if import settings
    // don't explicitly specify a format.
    if (output_format != DXGI_FORMAT_UNKNOWN)
    {
        goto _done;
    }

    if ((data->info.flags & texture_flags::is_hdr) ||
        image_format == DXGI_FORMAT_BC6H_UF16 || image_format == DXGI_FORMAT_BC6H_SF16)
    {
        output_format = DXGI_FORMAT_BC6H_UF16;
    }
    // If the source image is gray scale or a single channel block compressed format (BC4),
    // then output format will be BC4.
    else if (image_format == DXGI_FORMAT_R8_UNORM || image_format == DXGI_FORMAT_BC4_UNORM || image_format == DXGI_FORMAT_BC4_SNORM)
    {
        output_format = DXGI_FORMAT_BC4_UNORM;
    }
    // Test if the source image is a normal map and if so, use BC5 format for the output.
    else if (is_normal_map(image) || image_format == DXGI_FORMAT_BC5_UNORM || image_format == DXGI_FORMAT_BC5_SNORM)
    {
        data->info.flags |= texture_flags::is_imported_as_normal_map;
        output_format = DXGI_FORMAT_BC5_UNORM;

        if (IsSRGB(image_format))
        {
            scratch.OverrideFormat(MakeTypelessUNORM(MakeTypeless(image_format)));
        }
    }
    // We exhausted all options. use an RGBA block compressed format.
    else
    {
        output_format = data->import_settings.prefer_bc7 ? DXGI_FORMAT_BC7_UNORM :
            scratch.IsAlphaAllOpaque() ? DXGI_FORMAT_BC1_UNORM : DXGI_FORMAT_BC3_UNORM;
    }

_done:
    assert(IsCompressed(output_format));
    if (HasAlpha(output_format)) data->info.flags |= texture_flags::has_alpha;

    return IsSRGB(image_format) ? MakeSRGB(output_format) : output_format;
}

bool
can_use_gpu(DXGI_FORMAT format)
{
    switch (format)
    {
    case DXGI_FORMAT_BC6H_TYPELESS:
    case DXGI_FORMAT_BC6H_UF16:
    case DXGI_FORMAT_BC6H_SF16:
    case DXGI_FORMAT_BC7_TYPELESS:
    case DXGI_FORMAT_BC7_UNORM:
    case DXGI_FORMAT_BC7_UNORM_SRGB:
    {
        std::lock_guard lock{ device_creation_mutex };
        static bool try_once = false;
        if (!try_once)
        {
            try_once = true;
            create_device();
        }

        return d3d11_devices.size() > 0;
    }
    }

    return false;
}

[[nodiscard]] ScratchImage
compress_image(texture_data *const data, ScratchImage& scratch)
{
    assert(data && data->import_settings.compress && scratch.GetImages());

    const Image *const image{ scratch.GetImage(0, 0, 0) };
    if (!image)
    {
        data->info.import_error = import_error::unknown;
        return {};
    }

    const DXGI_FORMAT output_format{ determine_output_format(data, scratch, image) };
    HRESULT hr{ S_OK };
    ScratchImage bc_scratch;
    if (can_use_gpu(output_format))
    {
        bool wait{ true };
        while (wait)
        {
            for (u32 i{ 0 }; i < d3d11_devices.size(); ++i)
            {
                if (d3d11_devices[i].hw_compression_mutex.try_lock())
                {
                    hr = Compress(d3d11_devices[i].device.Get(), scratch.GetImages(), scratch.GetImageCount(),
                                  scratch.GetMetadata(), output_format, TEX_COMPRESS_DEFAULT, 1.0f, bc_scratch);
                    d3d11_devices[i].hw_compression_mutex.unlock();
                    wait = false;
                    break;
                }
            }
            if (wait) std::this_thread::sleep_for(std::chrono::milliseconds(200));
        }
    }
    else
    {
        hr = Compress(scratch.GetImages(), scratch.GetImageCount(), scratch.GetMetadata(),
                      output_format, TEX_COMPRESS_PARALLEL, data->import_settings.alpha_threshold, bc_scratch);
    }

    if (FAILED(hr))
    {
        data->info.import_error = import_error::compress;
        return {};
    }

    return bc_scratch;
}
} // anonymous namespace

void
ShutDownTextureTools()
{
    d3d11_devices.clear();

    if (dxgi_module)
    {
        FreeLibrary(dxgi_module);
        dxgi_module = nullptr;
    }

    if (d3d11_module)
    {
        FreeLibrary(d3d11_module);
        d3d11_module = nullptr;
    }
}

EDITOR_INTERFACE void
Decompress(texture_data *const data)
{
    using namespace primal::content;
    assert(data->import_settings.compress);
    texture_info& info{ data->info };
    const DXGI_FORMAT format{ (DXGI_FORMAT)info.format };
    assert(IsCompressed(format));
    utl::vector<Image> images = subresource_data_to_images(data);
    const bool is_3d{ (info.flags & texture_flags::is_volume_map) != 0 };

    TexMetadata metadata{};
    metadata.width = info.width;
    metadata.height = info.height;
    metadata.depth = is_3d ? info.array_size : 1;
    metadata.arraySize = is_3d ? 1 : info.array_size;
    metadata.mipLevels = info.mip_levels;
    metadata.miscFlags = info.flags & texture_flags::is_cube_map ? TEX_MISC_TEXTURECUBE : 0;
    metadata.miscFlags2 = info.flags & texture_flags::is_premultiplied_alpha
        ? TEX_ALPHA_MODE_PREMULTIPLIED
        : info.flags & texture_flags::has_alpha ? TEX_ALPHA_MODE_STRAIGHT : TEX_ALPHA_MODE_OPAQUE;
    metadata.format = format;
    // TODO: what about 1D?
    metadata.dimension = is_3d ? TEX_DIMENSION_TEXTURE3D : TEX_DIMENSION_TEXTURE2D;

    ScratchImage scratch;
    HRESULT hr{ Decompress(images.data(), (size_t)images.size(), metadata, DXGI_FORMAT_UNKNOWN, scratch) };
    if (SUCCEEDED(hr))
    {
        copy_subresources(scratch, data);
        texture_info_from_metadata(scratch.GetMetadata(), data->info);
    }
    else
    {
        info.import_error = import_error::decompress;
    }
}

EDITOR_INTERFACE void
Import(texture_data *const data)
{
    const texture_import_settings& settings{ data->import_settings };
    assert(settings.sources && settings.source_count);

    utl::vector<ScratchImage> scratch_images;
    utl::vector<Image> images;

    u32 width{ 0 };
    u32 height{ 0 };
    DXGI_FORMAT format{};
    utl::vector<std::string> files = split(settings.sources, ';');
    assert(files.size() == settings.source_count);

    for (u32 i{ 0 }; i < settings.source_count; ++i)
    {
        scratch_images.emplace_back(load_from_file(data, files[i].c_str()));
        if (data->info.import_error) return;

        const ScratchImage& scratch{ scratch_images.back() };
        const TexMetadata& metadata{ scratch.GetMetadata() };

        if (i == 0)
        {
            width = (u32)metadata.width;
            height = (u32)metadata.height;
            format = metadata.format;
        }

        // All image sources should have the same size.
        if (width != metadata.width || height != metadata.height)
        {
            data->info.import_error = import_error::size_mismatch;
            return;
        }

        // All image sources should have the same format.
        if (format != metadata.format)
        {
            data->info.import_error = import_error::format_mismatch;
            return;
        }

        const u32 array_size{ (u32)metadata.arraySize };
        const u32 depth{ (u32)metadata.depth };

        for (u32 array_index{ 0 }; array_index < array_size; ++array_index)
            for (u32 depth_index{ 0 }; depth_index < depth; ++depth_index)
            {
                const Image* image{ scratch.GetImage(0, array_index, depth_index) };
                assert(image);

                if (!image)
                {
                    data->info.import_error = import_error::unknown;
                    return;
                }

                if (width != image->width || height != image->height)
                {
                    data->info.import_error = import_error::size_mismatch;
                    return;
                }

                images.emplace_back(*image);
            }
    }

    ScratchImage scratch{ initialize_from_images(data, images) };
    if (data->info.import_error) return;

    if (settings.compress)
    {
        ScratchImage bc_scratch{ compress_image(data, scratch) };
        if (data->info.import_error) return;

        // Decompress the first image to be used for the icon.
        assert(bc_scratch.GetImages());
        copy_icon(bc_scratch.GetImages()[0], data);

        scratch = std::move(bc_scratch);
    }

    copy_subresources(scratch, data);
    texture_info_from_metadata(scratch.GetMetadata(), data->info);
}
}
