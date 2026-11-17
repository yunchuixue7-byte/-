using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Gomoku.Server;

/// <summary>账号记录：密码以 PBKDF2 加盐哈希保存，战绩用于按胜率匹配。</summary>
public sealed record Account(string Name, int Wins, int Losses, int Draws)
{
    /// <summary>胜率；没打过棋的按 50% 计，避免新手被极端值拉走。</summary>
    public double WinRate => Wins + Losses == 0 ? 0.5 : (double)Wins / (Wins + Losses);
}

/// <summary>
/// 账号存储：SQLite 单文件数据库，保存用户名、密码哈希与胜负平计数。
/// 每次操作独立开连接并加锁，避免多线程并发写同一文件。
/// </summary>
public sealed class AccountStore
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100000;

    private readonly string _connectionString;
    private readonly object _gate = new();

    public AccountStore(string dbPath)
    {
        string? dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    /// <summary>建表：users 存账号，matches 存每一局的结束记录（便于后续统计与回放）。</summary>
    private void EnsureSchema()
    {
        using SqliteConnection conn = Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS users (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                name    TEXT NOT NULL UNIQUE,
                salt    TEXT NOT NULL,
                hash    TEXT NOT NULL,
                wins    INTEGER NOT NULL DEFAULT 0,
                losses  INTEGER NOT NULL DEFAULT 0,
                draws   INTEGER NOT NULL DEFAULT 0,
                created TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS matches (
                id     INTEGER PRIMARY KEY AUTOINCREMENT,
                black  TEXT NOT NULL,
                white  TEXT NOT NULL,
                winner INTEGER NOT NULL,
                reason TEXT NOT NULL,
                ended  TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>注册新账号；用户名重复或格式不合规时返回 null 并给出中文原因。</summary>
    public Account? Register(string name, string password, out string error)
    {
        name = name.Trim();
        if (!IsValidName(name))
        {
            error = "用户名需 2-16 个字符，只能包含中英文、数字、下划线";
            return null;
        }

        if (password.Length < 4)
        {
            error = "密码至少 4 位";
            return null;
        }

        lock (_gate)
        {
            using SqliteConnection conn = Open();
            using (SqliteCommand query = conn.CreateCommand())
            {
                query.CommandText = "SELECT COUNT(1) FROM users WHERE name = $name";
                query.Parameters.AddWithValue("$name", name);
                using SqliteDataReader reader = query.ExecuteReader();
                if (reader.Read() && reader.GetInt32(0) > 0)
                {
                    error = "该用户名已被注册，请换一个";
                    return null;
                }
            }

            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            using SqliteCommand insert = conn.CreateCommand();
            insert.CommandText =
                "INSERT INTO users(name, salt, hash, created) VALUES($name, $salt, $hash, $created)";
            insert.Parameters.AddWithValue("$name", name);
            insert.Parameters.AddWithValue("$salt", Convert.ToBase64String(salt));
            insert.Parameters.AddWithValue("$hash", Hash(password, salt));
            insert.Parameters.AddWithValue("$created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            insert.ExecuteNonQuery();

            error = string.Empty;
            return new Account(name, 0, 0, 0);
        }
    }

    /// <summary>校验用户名与密码；失败时不区分"用户不存在"和"密码错"，避免试探账号。</summary>
    public Account? Login(string name, string password, out string error)
    {
        name = name.Trim();
        lock (_gate)
        {
            using SqliteConnection conn = Open();
            using SqliteCommand query = conn.CreateCommand();
            query.CommandText =
                "SELECT salt, hash, wins, losses, draws FROM users WHERE name = $name";
            query.Parameters.AddWithValue("$name", name);

            using SqliteDataReader reader = query.ExecuteReader();
            if (!reader.Read())
            {
                error = "用户名或密码不正确";
                return null;
            }

            byte[] salt = Convert.FromBase64String(reader.GetString(0));
            string stored = reader.GetString(1);
            int wins = reader.GetInt32(2);
            int losses = reader.GetInt32(3);
            int draws = reader.GetInt32(4);
            reader.Close();

            if (!Verify(password, salt, stored))
            {
                error = "用户名或密码不正确";
                return null;
            }

            error = string.Empty;
            return new Account(name, wins, losses, draws);
        }
    }

    /// <summary>读某人的最新战绩（登录后用于匹配时计算胜率）。</summary>
    public Account? Get(string name)
    {
        lock (_gate)
        {
            using SqliteConnection conn = Open();
            using SqliteCommand query = conn.CreateCommand();
            query.CommandText = "SELECT wins, losses, draws FROM users WHERE name = $name";
            query.Parameters.AddWithValue("$name", name);
            using SqliteDataReader reader = query.ExecuteReader();
            return reader.Read()
                ? new Account(name, reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2))
                : null;
        }
    }

    /// <summary>
    /// 记一局结果：写 matches 明细，并按胜方更新 users 的胜负平计数。
    /// 名字为 null 表示游客，不写账号计数（但仍记入 matches 明细）。
    /// winner：0=黑胜，1=白胜，-1=平局。
    /// </summary>
    public void RecordResult(string? blackName, string? whiteName, int winner, string reason)
    {
        lock (_gate)
        {
            using SqliteConnection conn = Open();
            using (SqliteCommand insert = conn.CreateCommand())
            {
                insert.CommandText =
                    "INSERT INTO matches(black, white, winner, reason, ended) VALUES($b, $w, $win, $r, $t)";
                insert.Parameters.AddWithValue("$b", blackName ?? "游客");
                insert.Parameters.AddWithValue("$w", whiteName ?? "游客");
                insert.Parameters.AddWithValue("$win", winner);
                insert.Parameters.AddWithValue("$r", reason);
                insert.Parameters.AddWithValue("$t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                insert.ExecuteNonQuery();
            }

            switch (winner)
            {
                case 0:
                    Bump(conn, blackName, "wins");
                    Bump(conn, whiteName, "losses");
                    break;
                case 1:
                    Bump(conn, whiteName, "wins");
                    Bump(conn, blackName, "losses");
                    break;
                default:
                    Bump(conn, blackName, "draws");
                    Bump(conn, whiteName, "draws");
                    break;
            }
        }
    }

    private static void Bump(SqliteConnection conn, string? name, string column)
    {
        if (name is null)
        {
            return; // 游客不记战绩
        }

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE users SET {column} = {column} + 1 WHERE name = $name";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    private static bool IsValidName(string name)
    {
        if (name.Length is < 2 or > 16)
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string Hash(string password, byte[] salt)
        => Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes));

    private static bool Verify(string password, byte[] salt, string stored)
    {
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(stored));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
