using Microsoft.Data.Sqlite;   // ← 추가: SQLite 데이터베이스를 여는 도구
using System.Collections;
using System.Text.Json;        // 씨앗 데이터(Data/monsters.json)를 읽을 때만 쓴다
using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// ── 2회차. DB 파일 준비 ────────────────────────────────────────────────
// Data/game.db 파일 하나가 데이터베이스다. 없으면 처음 열 때 만들어진다
var dbPath = Path.Combine(app.Environment.ContentRootPath, "Data", "game.db");
var connectionString = $"Data Source={dbPath}";

// 테이블이 없으면 만든다. 있으면 그대로 둔다 (IF NOT EXISTS)
using (var conn = new SqliteConnection(connectionString))
{
    conn.Open();
    var create = conn.CreateCommand();
    create.CommandText = """
        CREATE TABLE IF NOT EXISTS monsters (
            id     INTEGER PRIMARY KEY AUTOINCREMENT,
            name   TEXT    NOT NULL,
            hp     INTEGER NOT NULL,
            attack INTEGER NOT NULL,
            speed  REAL    NOT NULL
        )
        """;
    create.ExecuteNonQuery();

    // 테이블이 비어 있을 때만 1회차의 monsters.json을 초기 데이터로 넣는다. 그래서 재시작해도 중복되지 않는다
    var count = conn.CreateCommand();
    count.CommandText = "SELECT COUNT(*) FROM monsters";
    if ((long)count.ExecuteScalar()! == 0)
    {
        var seedPath = Path.Combine(app.Environment.ContentRootPath, "Data", "monsters.json");
        var seed = JsonSerializer.Deserialize<List<Monster>>(
            File.ReadAllText(seedPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (var m in seed)
        {
            var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO monsters (name, hp, attack, speed) VALUES ($name, $hp, $attack, $speed)";
            insert.Parameters.AddWithValue("$name", m.Name);
            insert.Parameters.AddWithValue("$hp", m.Hp);
            insert.Parameters.AddWithValue("$attack", m.Attack);
            insert.Parameters.AddWithValue("$speed", m.Speed);
            insert.ExecuteNonQuery();
        }
    }
}

// SELECT 결과(행들)를 Monster 목록으로 바꾸는 헬퍼. GET 네 곳에서 같이 쓴다



static List<Monster> ReadMonsters(SqliteDataReader reader)
{
    var list = new List<Monster>();
    while (reader.Read()) // 한 줄(한 마리)씩
    {

        list.Add(new Monster
        {
            Id = reader.GetInt32(0),
            Name = reader.GetString(1),
            Hp = reader.GetInt32(2),
            Attack = reader.GetInt32(3),
            Speed = reader.GetFloat(4)
        });
    }
    return list;
}
// 모든 몬스터를 DB에서 읽는다. /monsters, /monsters/slow, /monsters/big이 같이 쓴다
static List<Monster> LoadAllMonsters(string connectionString)
{
    using var conn = new SqliteConnection(connectionString);
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, name, hp, attack, speed FROM monsters ORDER BY id";
    using var reader = cmd.ExecuteReader();
    return ReadMonsters(reader);
}



app.MapGet("/hello", () => "안녕하세요, 서버입니다.");
// -몬스터 조회 - 이제 파일이 아니라 DB에서 읽는다
app.MapGet("/monsters", () => new { monsters = LoadAllMonsters(connectionString) });



// 몬스터 하나 — 없으면 404
//app.MapGet("/monsters/{id:int}", (int id) =>
//{
//    var found = monsters.Find(m => m.Id == id);
//    return found is null
//        ? Results.NotFound(new { error = $"id {id} 몬스터가 없습니다." })
//        : Results.Ok(found);
//});

app.Run("http://localhost:5000");

app.MapGet("/monsters/{id:int}", (int id) =>
{
    using var conn = new SqliteConnection(connectionString);
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id, name, hp, attack, speed FROM monsters WHERE id = $id";
    cmd.Parameters.AddWithValue("$id", id);   // 값은 글자로 이어 붙이지 않고 파라미터로 넘긴다
    using var reader = cmd.ExecuteReader();
    var found = ReadMonsters(reader);
    return found.Count == 0
        ? Results.NotFound(new { error = $"id {id} 몬스터가 없습니다." })
        : Results.Ok(found[0]);
});

// ── 2회차. 몬스터 추가 — POST는 "받아라". 본문의 JSON이 Monster로 들어온다 ──
app.MapPost("/monsters", (Monster m) =>
{
    if (string.IsNullOrWhiteSpace(m.Name))
        return Results.BadRequest(new { error = "name은 비어 있으면 안 됩니다." });

    using var conn = new SqliteConnection(connectionString);
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = """
        INSERT INTO monsters (name, hp, attack, speed) VALUES ($name, $hp, $attack, $speed);
        SELECT last_insert_rowid();
        """;
    cmd.Parameters.AddWithValue("$name", m.Name);
    cmd.Parameters.AddWithValue("$hp", m.Hp);
    cmd.Parameters.AddWithValue("$attack", m.Attack);
    cmd.Parameters.AddWithValue("$speed", m.Speed);
    m.Id = (int)(long)cmd.ExecuteScalar()!;   // DB가 붙여 준 새 id

    return Results.Created($"/monsters/{m.Id}", m);   // 201 Created + 새 몬스터
});


// 데이터 모양
class Monster
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Hp { get; set; }
    public int Attack { get; set; }
    public float Speed { get; set; }
}