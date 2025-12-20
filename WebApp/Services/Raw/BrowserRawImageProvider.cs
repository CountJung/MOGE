using SharedUI.Services.Raw;

namespace WebApp.Services.Raw;

internal sealed class BrowserRawImageProvider : IRawImageCache
{
    private readonly object _gate = new();

    private const int MaxEntries = 80;
    private readonly Dictionary<string, RawRgbaImage> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LinkedListNode<string>> _nodes = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lru = new();

    public void Set(string signature, int width, int height, byte[] rgbaBytes)
    {
        Set(signature, new RawRgbaImage(width, height, rgbaBytes));
    }

    public void Set(string signature, RawRgbaImage image)
    {
        lock (_gate)
        {
            if (_nodes.TryGetValue(signature, out var node))
            {
                _cache[signature] = image;
                _lru.Remove(node);
                _lru.AddFirst(node);
            }
            else
            {
                _cache[signature] = image;
                var newNode = _lru.AddFirst(signature);
                _nodes[signature] = newNode;
            }

            while (_cache.Count > MaxEntries)
            {
                var last = _lru.Last;
                if (last is null)
                    break;

                var key = last.Value;
                _lru.RemoveLast();
                _nodes.Remove(key);
                _cache.Remove(key);
            }
        }
    }

    public bool TryGet(string signature, out RawRgbaImage image)
    {
        lock (_gate)
        {
            if (!_cache.TryGetValue(signature, out image))
            {
                image = default;
                return false;
            }

            if (_nodes.TryGetValue(signature, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
            }

            return image.RgbaBytes is { Length: > 0 } && image.Width > 0 && image.Height > 0;
        }
    }
}
