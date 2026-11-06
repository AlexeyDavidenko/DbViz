namespace DbViz.Core.Model;

/// <summary>Human-readable rendering of Oracle data types.</summary>
public static class OracleTypes
{
    public static string Format(ColumnRow c) =>
        Format(c.DataType, c.DataTypeOwner, c.DataLength, c.DataPrecision, c.DataScale, c.CharLength, c.CharUsed);

    public static string Format(string? dataType, string? owner, int? length, int? precision, int? scale, int? charLength, string? charUsed)
    {
        if (string.IsNullOrEmpty(dataType)) return "?";
        var t = dataType;
        switch (t)
        {
            case "VARCHAR2" or "CHAR":
                return charUsed == "C" ? $"{t}({charLength} CHAR)" : $"{t}({length})";
            case "NVARCHAR2" or "NCHAR":
                return $"{t}({charLength ?? length})";
            case "RAW":
                return $"RAW({length})";
            case "NUMBER":
                if (precision is null && scale is null) return "NUMBER";
                if (precision is null) return scale == 0 ? "INTEGER" : $"NUMBER(*,{scale})";
                return scale is null or 0 ? $"NUMBER({precision})" : $"NUMBER({precision},{scale})";
            case "FLOAT":
                return precision is null ? "FLOAT" : $"FLOAT({precision})";
        }
        return string.IsNullOrEmpty(owner) || owner == "SYS" || owner == "PUBLIC" ? t : $"{owner}.{t}";
    }

    public static string Format(ArgumentRow a)
    {
        // %ROWTYPE / record / package collection: TYPE_OWNER.TYPE_NAME.TYPE_SUBNAME
        if (a.TypeName is not null && a.DataType is "PL/SQL RECORD" or "TABLE" or "VARRAY" or "OBJECT" or "REF" or "PL/SQL TABLE" or "REF CURSOR" or "UNDEFINED")
        {
            var name = a.TypeSubname is null ? a.TypeName : $"{a.TypeName}.{a.TypeSubname}";
            return a.TypeOwner is null ? name : $"{a.TypeOwner}.{name}";
        }
        if (a.DataType is not null && a.DataType.StartsWith("OPAQUE/", StringComparison.Ordinal) && a.TypeName is not null)
            return a.TypeOwner is null or "SYS" or "PUBLIC" ? a.TypeName : $"{a.TypeOwner}.{a.TypeName}"; // OPAQUE/XMLTYPE → XMLTYPE
        if (a.PlsType is not null && a.DataType is not ("PL/SQL RECORD" or "TABLE")) return a.PlsType;
        return a.DataType ?? "?";
    }
}
