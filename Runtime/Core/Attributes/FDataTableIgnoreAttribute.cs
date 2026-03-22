using System;

namespace FDataTable.Runtime
{
    /// <summary>
    /// Mark a field or property to be ignored by FDataTable editor window.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class FDataTableIgnoreAttribute : Attribute { }
}
