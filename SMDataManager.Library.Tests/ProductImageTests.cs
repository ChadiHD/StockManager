using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// A product's photographs and the one every card shows (T11). The procedures keep
/// Product.ProductImage pointing at the first, so every page that already rendered that column
/// shows the store's own photograph with no change to any of them.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductImageTests
{
    private const string First = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg";
    private const string Second = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.png";

    [SkippableFact]
    public void TheFirstPhotographIsTheProductsImageUntilItIsRemoved()
    {
        Skip.IfNot(TestDatabase.IsConfigured, TestDatabase.SkipReason);

        var (connection, transaction) = TestDatabase.OpenRollbackScope();
        using (connection)
        using (transaction)
        {
            // A content provider's photograph, which the store's own replaces.
            var product = Scalar<int>(connection, transaction, """
                INSERT INTO dbo.Product (ProductName, [Description], RetailPrice, Sku, QuantityInStock, ProductImage)
                OUTPUT INSERTED.Id
                VALUES (N'Shirt', N'A shirt.', 40, @sku, 1, N'https://images.example.com/shirt.jpg');
                """, ("@sku", $"IMG-{Guid.NewGuid():N}"[..20]));

            Add(connection, transaction, product, First);
            Add(connection, transaction, product, Second);

            Image(connection, transaction, product).Should().Be("/img/" + First);
            Names(connection, transaction, product).Should().Equal(First, Second);

            Remove(connection, transaction, product, First).Should().BeTrue();
            Image(connection, transaction, product).Should().Be("/img/" + Second);

            // Another product's name removes nothing, so the caller deletes no bytes for it.
            Remove(connection, transaction, product, "cccccccccccccccccccccccccccccccc.jpg").Should().BeFalse();

            Remove(connection, transaction, product, Second).Should().BeTrue();
            Image(connection, transaction, product).Should().BeNull("an address naming nothing is a broken image");
        }
    }

    private static void Add(SqlConnection connection, SqlTransaction transaction, int product, string name)
    {
        using var command = new SqlCommand(
            "EXEC dbo.spProductImage_Add @ProductId = @p, @StoredName = @n, @ContentType = N'image/jpeg';",
            connection, transaction);
        command.Parameters.AddWithValue("@p", product);
        command.Parameters.AddWithValue("@n", name);
        command.ExecuteNonQuery();
    }

    private static bool Remove(SqlConnection connection, SqlTransaction transaction, int product, string name) =>
        Scalar<int>(connection, transaction, "EXEC dbo.spProductImage_Remove @ProductId = @p, @StoredName = @n;",
            ("@p", product), ("@n", name)) > 0;

    private static string? Image(SqlConnection connection, SqlTransaction transaction, int product) =>
        Scalar<object>(connection, transaction, "SELECT ProductImage FROM dbo.Product WHERE Id = @p;", ("@p", product)) as string;

    private static List<string> Names(SqlConnection connection, SqlTransaction transaction, int product)
    {
        using var command = new SqlCommand("EXEC dbo.spProductImage_GetByProduct @ProductId = @p;", connection, transaction);
        command.Parameters.AddWithValue("@p", product);
        using var reader = command.ExecuteReader();

        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    private static T Scalar<T>(SqlConnection connection, SqlTransaction transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = new SqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (T)command.ExecuteScalar()!;
    }
}
