using Microsoft.Data.SqlClient;

namespace AriaSignature.Api.Contracts;

public static class MsSqlConnectionStringBuilder
{
    public static string Build(MsSqlConnectionPayload p)
    {
        var csb = new SqlConnectionStringBuilder
        {
            DataSource = p.Server.Trim(),
            InitialCatalog = p.Database.Trim(),
            TrustServerCertificate = p.TrustServerCertificate,
            ConnectTimeout = 20,
            Encrypt = true
        };

        if (string.Equals(p.Auth, "windows", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Auth, "integrated", StringComparison.OrdinalIgnoreCase))
        {
            csb.IntegratedSecurity = true;
        }
        else
        {
            csb.UserID = p.User?.Trim() ?? string.Empty;
            csb.Password = p.Password ?? string.Empty;
        }

        return csb.ConnectionString;
    }

    public static Dictionary<string, string[]>? ValidatePayload(MsSqlConnectionPayload p)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(p.Server))
        {
            errors["msSql.server"] = ["Укажите сервер SQL (например localhost или SERVER\\INSTANCE)"];
        }

        if (string.IsNullOrWhiteSpace(p.Database))
        {
            errors["msSql.database"] = ["Укажите имя базы данных"];
        }

        var isWindows = string.Equals(p.Auth, "windows", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Auth, "integrated", StringComparison.OrdinalIgnoreCase);
        if (!isWindows)
        {
            if (string.IsNullOrWhiteSpace(p.User))
            {
                errors["msSql.user"] = ["Укажите логин SQL или выберите аутентификацию Windows"];
            }
        }

        return errors.Count > 0 ? errors : null;
    }
}
