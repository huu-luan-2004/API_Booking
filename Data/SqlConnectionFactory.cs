using Microsoft.Data.SqlClient;
using System.Data;

namespace HotelBookingApi.Data;

public class SqlConnectionFactory
{
    private readonly IConfiguration _config;
    public SqlConnectionFactory(IConfiguration config) => _config = config;

    public IDbConnection Create()
    {
        var cs = _config.GetConnectionString("SqlServer") ?? _config["SQLSERVER_CONNECTION_STRING"] ?? string.Empty;
        return new SqlConnection(cs);
    }
}
