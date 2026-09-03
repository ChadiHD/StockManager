namespace SMPortal.Services;

// Razor evaluates a property every time the markup touches it — the row loop, the result count
// and the pager total would each re-run the fuzzy scoring. This memoises the result for a given
// filter/search/list-size key so it is computed once per change instead.
public sealed class SearchCache<T>
{
    private List<T>? _items;
    private string _key = "";

    public List<T> Get(string key, Func<IEnumerable<T>> compute)
    {
        if (_items is null || _key != key)
        {
            _items = compute().ToList();
            _key = key;
        }

        return _items;
    }
}
