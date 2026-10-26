using FamilyTree.Infrastructure;

// Brings the family tree database up to date. Run by the deploy pipeline as the
// database administrator, and locally to create or update your development database.
//
//   dotnet run --project src/FamilyTree.DbMigrator -- --connection "<connection string>"
//       [--grant-read-write <identity name> <identity client ID>]
//
// --grant-read-write (Azure only) also makes sure that managed identity is a database
// user with read and write access to data, and nothing more.

string? connectionString = null;
(string Name, Guid ClientId)? grantee = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--connection" when i + 1 < args.Length:
            connectionString = args[++i];
            break;
        case "--grant-read-write" when i + 2 < args.Length && Guid.TryParse(args[i + 2], out var clientId):
            grantee = (args[i + 1], clientId);
            i += 2;
            break;
        default:
            Console.Error.WriteLine($"Unexpected argument '{args[i]}'. Usage: --connection \"<connection string>\" [--grant-read-write <identity name> <identity client ID>]");
            return 2;
    }
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("--connection is required.");
    return 2;
}

Console.WriteLine("Applying migrations…");
await DatabaseDeployment.MigrateAsync(connectionString);
Console.WriteLine("The database schema is up to date.");

if (grantee is { } identity)
{
    Console.WriteLine($"Granting {identity.Name} read and write access to data…");
    await DatabaseDeployment.GrantReadWriteAsync(connectionString, identity.Name, identity.ClientId);
    Console.WriteLine("Done.");
}

return 0;
