using Dapper;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace SMDataManager.Library.DataAccess
{
    /// <summary>
    /// <c>dbo.ProductOptionList</c> as a table-valued parameter (T11): a new variant's options or
    /// a shopper's filter. The order given is the SortOrder written, so Size before Colour is the
    /// caller's to say.
    /// </summary>
    internal static class ProductOptionTable
    {
        public static SqlMapper.ICustomQueryParameter From(IEnumerable<(string Name, string Value)> options)
        {
            var table = new DataTable();
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Value", typeof(string));
            table.Columns.Add("SortOrder", typeof(int));

            // Distinct, because the type is keyed on the pair and a repeated one would fail as a key
            // violation rather than mean anything.
            var order = 0;
            foreach (var (name, value) in options.Distinct()) table.Rows.Add(name, value, order++);

            return table.AsTableValuedParameter("dbo.ProductOptionList");
        }
    }
}
