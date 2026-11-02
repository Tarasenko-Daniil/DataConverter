using System.Globalization;
 
namespace DataConverter.Models;

public class DataRecord
{
    private Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private List<string> _order = new();

    public int Count
    {
        get
        {
            return _order.Count;
        }
    }

    public List<KeyValuePair<string, object?>> Fields
    {
        get
        {
            var result = new List<KeyValuePair<string, object?>>();

            foreach (var key in _order)
            {
                var value = _values[key];
                result.Add(new KeyValuePair<string, object?>(key, value));
            }

            return result;
        }
    }
 
    public bool Contains(string name)
    { 
        return _values.ContainsKey(name);
    }

    public object? Get(string name)
    {
        if (_values.ContainsKey(name))
            return _values[name];

        return null;
    } 

    public void Set(string name, object? value)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name), "Имя поля не может быть null.");
        
        if (_values.ContainsKey(name) == false)
            _order.Add(name);
        _values[name] = value;
    }
    
    private static int ParseIndex(string segment, int listLength)
    {
        if (int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) == false)
            return -1;

        if (index >= listLength)
            return -1;

        return index;
    }
    
    public object? GetPath(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path), "Имя поля не может быть null.");
        }

        if (Contains(path))
            return Get(path);

        object? current = this;

        foreach (var segment in path.Split('.'))
        {
            if (current is DataRecord record && record.Contains(segment))
            {
                current = record._values[segment];
            }
            else if (current is List<object?> list)
            {
                var index = ParseIndex(segment, list.Count);
                if (index < 0)
                    return null;

                current = list[index];
            }
            else
            {
                return null;
            }
        }

        return current;
    }
    
    public void SetPath(string path, object? value)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path), "Имя поля не может быть null.");
        }

        var segments = path.Split('.');
        var current = this;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var name = segments[i];

            if (current.Contains(name))
            {
                var existing = current.Get(name);

                if (existing is not DataRecord nested)
                    throw new InvalidOperationException($"Нельзя создать '{path}': '{name}' уже содержит не объект.");

                current = nested;
            }
            else
            {
                var created = new DataRecord();
                current.Set(name, created);
                current = created;
            }
        }

        current.Set(segments[^1], value);
    }
}