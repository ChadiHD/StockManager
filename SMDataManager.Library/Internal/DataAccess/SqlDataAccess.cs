using System;
using System.Collections.Generic;
using System.Configuration;
using Microsoft.Extensions.Configuration;
using Dapper;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace SMDataManager.Library.Internal.DataAccess
{
    public class SqlDataAccess : IDisposable, ISqlDataAccess
    {
        /// <summary>
        /// Retrieves a connection string for our db based on configuration file
        /// </summary>
        /// <param name="name">A connection string name to look for in configuration file</param>
        /// <returns>the connection string</returns>
        private readonly IConfiguration _config;
        private readonly ILogger<SqlDataAccess> _logger;

        public SqlDataAccess(IConfiguration config, ILogger<SqlDataAccess> logger)
        {
            _config = config;
            _logger = logger;
        }
        public string GetConnectionString(string name)
        {
            return _config.GetConnectionString(name);
        }

        // Using Dapper as a Micro ORM
        public List<T> LoadData<T, U>(string storeProcedure, U parameters, string connectionStringName)
        {
            string connectionString = GetConnectionString(connectionStringName);

            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                List<T> rows = connection.Query<T>(storeProcedure, parameters,
                    commandType: CommandType.StoredProcedure).ToList();

                return rows;
            }
        }

        public void SaveData<T>(string storeProcedure, T parameters, string connectionStringName)
        {
            string connectionString = GetConnectionString(connectionStringName);

            using (IDbConnection connection = new SqlConnection(connectionString))
            {
                connection.Execute(storeProcedure, parameters,
                    commandType: CommandType.StoredProcedure);
            }
        }

        // Start transaction method
        private IDbConnection _connection;
        private IDbTransaction _transaction;

        public void StartTransaction(string cnnStringName)
        {
            string connectionString = GetConnectionString(cnnStringName);

            _connection = new SqlConnection(connectionString);

            _connection.Open();

            _transaction = _connection.BeginTransaction();

            isClosed = false;
        }
        // Load using the transaction
        public List<T> LoadDataInTransaction<T, U>(string storeProcedure, U parameters)
        {
            List<T> rows = _connection.Query<T>(storeProcedure, parameters,
                commandType: CommandType.StoredProcedure, transaction: _transaction).ToList();

            return rows;
        }

        // Save the transaction
        public void SaveDataInTransaction<T>(string storeProcedure, T parameters)
        {
            _connection.Execute(storeProcedure, parameters,
                commandType: CommandType.StoredProcedure, transaction: _transaction);
        }

        private bool isClosed = false;
        // Stop transaction method
        // If transaction is successful
        public void CommitTransaction()
        {
            _transaction?.Commit();
            _connection?.Close();

            isClosed = true;
        }
        // If transaction fails
        public void RollbackTransaction()
        {
            _transaction?.Rollback();
            _connection?.Close();

            isClosed = true;
        }

        // Dispose transaction
        public void Dispose()
        {
            if (isClosed == true)
            {
                try
                {
                    CommitTransaction();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Commit transaction failed in the dispose method.");
                }
            }

            _transaction = null;
            _connection = null;
        }
    }
}
