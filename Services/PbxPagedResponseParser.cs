using System.Text.Json;
using CallAnalog.Softphone.Models;

namespace CallAnalog.Softphone.Services;

internal static class PbxPagedResponseParser
{
    public static PagedResult<T> Parse<T>(JsonElement root, int currentPage, JsonSerializerOptions jsonOptions)
    {
        if (!root.TryGetProperty("data", out var dataElement))
        {
            return Empty<T>(currentPage);
        }

        return dataElement.ValueKind switch
        {
            JsonValueKind.Array => ParseItems<T>(dataElement, null, currentPage, jsonOptions),
            JsonValueKind.Object => ParseObjectWrapper<T>(dataElement, currentPage, jsonOptions),
            JsonValueKind.Null => Empty<T>(currentPage),
            _ => Empty<T>(currentPage)
        };
    }

    private static PagedResult<T> ParseObjectWrapper<T>(
        JsonElement dataWrapper,
        int currentPage,
        JsonSerializerOptions jsonOptions)
    {
        if (!dataWrapper.TryGetProperty("data", out var itemsElement)
            || itemsElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return BuildResult<T>([], dataWrapper, currentPage);
        }

        if (itemsElement.ValueKind != JsonValueKind.Array)
        {
            return BuildResult<T>([], dataWrapper, currentPage);
        }

        return ParseItems<T>(itemsElement, dataWrapper, currentPage, jsonOptions);
    }

    private static PagedResult<T> ParseItems<T>(
        JsonElement itemsElement,
        JsonElement? paginationWrapper,
        int currentPage,
        JsonSerializerOptions jsonOptions)
    {
        List<T> items;
        if (itemsElement.ValueKind == JsonValueKind.Array && itemsElement.GetArrayLength() > 0)
        {
            items = JsonSerializer.Deserialize<List<T>>(itemsElement.GetRawText(), jsonOptions) ?? [];
        }
        else
        {
            items = [];
        }

        return BuildResult(items, paginationWrapper, currentPage);
    }

    private static PagedResult<T> BuildResult<T>(
        List<T> items,
        JsonElement? paginationWrapper,
        int currentPage)
    {
        if (paginationWrapper is null || paginationWrapper.Value.ValueKind != JsonValueKind.Object)
        {
            return new PagedResult<T>
            {
                Items = items,
                CurrentPage = currentPage,
                LastPage = currentPage,
                Total = items.Count,
                LoadedCount = items.Count
            };
        }

        var wrapper = paginationWrapper.Value;
        var lastPage = ReadIntOrDefault(wrapper, "last_page", currentPage);
        var total = ReadIntOrDefault(wrapper, "total", items.Count);
        var loadedCount = ReadIntOrDefault(wrapper, "to", items.Count);

        return new PagedResult<T>
        {
            Items = items,
            CurrentPage = currentPage,
            LastPage = Math.Max(currentPage, lastPage),
            Total = total,
            LoadedCount = loadedCount
        };
    }

    private static int ReadIntOrDefault(JsonElement parent, string propertyName, int defaultValue)
    {
        if (!parent.TryGetProperty(propertyName, out var element)
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return defaultValue;
        }

        if (element.TryGetInt32(out var value))
        {
            return value;
        }

        if (element.ValueKind == JsonValueKind.String
            && int.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static PagedResult<T> Empty<T>(int currentPage) =>
        new()
        {
            CurrentPage = currentPage,
            LastPage = currentPage,
            Total = 0,
            LoadedCount = 0
        };
}
