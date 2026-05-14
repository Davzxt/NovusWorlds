using Godot;
using System;

public static class NovusTemplates
{
    public static NovusMap StormIsland()
    {
        var map = new NovusMap
        {
            Name = "NVX Storm Island",
            Description = "Round survival de desastres: lobby, ilha, abrigos, torre, meteoro, enchente e lava. Editavel no Novus Studio.",
            SkyColor = new Color(0.55f, 0.78f, 1f),
            Spawn = new Vector3(0, 5, 0),
            MaxPlayers = 20
        };

        Add(map, "Baseplate", "Part", new Vector3(0, -0.5f, 0), new Vector3(180, 1, 180), new Color(0.18f, 0.52f, 0.24f), "Grass");
        Add(map, "SpawnPoint", "SpawnPoint", new Vector3(0, 0.25f, 0), new Vector3(8, 0.5f, 8), new Color(0.1f, 0.95f, 0.28f), "Neon");
        Add(map, "Lobby Pad", "Part", new Vector3(0, 0.1f, 0), new Vector3(22, 0.25f, 22), new Color(0.25f, 0.7f, 1f), "Plastic");
        Add(map, "Storm Island", "Part", new Vector3(0, 0.05f, -42), new Vector3(76, 0.35f, 58), new Color(0.24f, 0.58f, 0.28f), "Grass");
        Add(map, "Ocean Kill Plane", "Part", new Vector3(0, -7f, 0), new Vector3(260, 1, 260), new Color(0.05f, 0.22f, 0.65f), "Glass").Transparency = 0.42f;

        Add(map, "Shelter Floor", "Part", new Vector3(-22, 0.8f, -45), new Vector3(20, 1, 18), new Color(0.55f, 0.55f, 0.55f), "Stone");
        Add(map, "Shelter Back Wall", "Part", new Vector3(-22, 5.3f, -54), new Vector3(20, 9, 1), new Color(0.42f, 0.42f, 0.42f), "Stone");
        Add(map, "Shelter Left Wall", "Part", new Vector3(-32, 5.3f, -45), new Vector3(1, 9, 18), new Color(0.42f, 0.42f, 0.42f), "Stone");
        Add(map, "Shelter Right Wall", "Part", new Vector3(-12, 5.3f, -45), new Vector3(1, 9, 18), new Color(0.42f, 0.42f, 0.42f), "Stone");
        Add(map, "Shelter Roof", "Part", new Vector3(-22, 10.1f, -45), new Vector3(22, 1, 20), new Color(0.36f, 0.36f, 0.36f), "Metal");

        Add(map, "Watch Tower Base", "Part", new Vector3(24, 0.85f, -48), new Vector3(14, 1, 14), new Color(0.45f, 0.25f, 0.12f), "Wood");
        for (var i = 0; i < 5; i++)
            Add(map, "Tower Step " + (i + 1), "Part", new Vector3(24, 1.4f + i * 1.25f, -40 + i * 2.6f), new Vector3(8, 0.6f, 3), new Color(0.5f, 0.28f, 0.12f), "Wood");
        Add(map, "Watch Tower Top", "Part", new Vector3(24, 8.8f, -26), new Vector3(16, 1, 16), new Color(0.5f, 0.28f, 0.12f), "Wood");
        Add(map, "Tower Rail A", "Part", new Vector3(24, 10.4f, -18), new Vector3(16, 2, 1), new Color(0.26f, 0.13f, 0.05f), "Wood");
        Add(map, "Tower Rail B", "Part", new Vector3(16, 10.4f, -26), new Vector3(1, 2, 16), new Color(0.26f, 0.13f, 0.05f), "Wood");
        Add(map, "Tower Rail C", "Part", new Vector3(32, 10.4f, -26), new Vector3(1, 2, 16), new Color(0.26f, 0.13f, 0.05f), "Wood");

        Add(map, "Volcano Core", "Cylinder", new Vector3(40, 8, -66), new Vector3(18, 16, 18), new Color(0.24f, 0.12f, 0.08f), "Stone");
        Add(map, "Lava Vent", "Cylinder", new Vector3(40, 16.5f, -66), new Vector3(10, 1, 10), new Color(1f, 0.15f, 0.02f), "Neon");
        Add(map, "Meteor Marker 1", "Ball", new Vector3(-2, 18, -42), new Vector3(5, 5, 5), new Color(0.9f, 0.45f, 0.08f), "Neon").Anchored = false;
        Add(map, "Meteor Marker 2", "Ball", new Vector3(14, 21, -58), new Vector3(4, 4, 4), new Color(0.9f, 0.45f, 0.08f), "Neon").Anchored = false;
        Add(map, "Meteor Marker 3", "Ball", new Vector3(-18, 24, -30), new Vector3(4, 4, 4), new Color(0.95f, 0.35f, 0.05f), "Neon").Anchored = false;
        Add(map, "Loose Disaster Brick A", "Part", new Vector3(4, 14, -42), new Vector3(8, 2, 4), new Color(0.75f, 0.1f, 0.08f), "Brick").Anchored = false;
        Add(map, "Loose Disaster Brick B", "Part", new Vector3(10, 17, -48), new Vector3(5, 5, 5), new Color(0.1f, 0.25f, 0.9f), "Plastic").Anchored = false;
        Add(map, "Flood Safe Rock", "Part", new Vector3(-42, 3, -28), new Vector3(16, 6, 14), new Color(0.4f, 0.4f, 0.43f), "Stone");

        for (var i = 0; i < 8; i++)
        {
            var angle = i * Mathf.Tau / 8f;
            var pos = new Vector3(Mathf.Cos(angle) * 34f, 1.8f, -42 + Mathf.Sin(angle) * 24f);
            Add(map, "Tree Trunk " + i, "Cylinder", pos, new Vector3(2, 4, 2), new Color(0.35f, 0.18f, 0.06f), "Wood");
            Add(map, "Tree Crown " + i, "Ball", pos + Vector3.Up * 3.2f, new Vector3(7, 7, 7), new Color(0.08f, 0.45f, 0.13f), "Grass");
        }

        map.Scripts.Add(new NovusScript
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "DisasterRoundController",
            Source = "local disasters = {\"Meteor Shower\", \"Flash Flood\", \"Volcanic Eruption\", \"Lightning Storm\"}\nlocal roundTime = 90\n\nprint(\"NVX Storm Island loaded\")\n\ngame.on(\"playerJoin\", function(player)\n  player:setHealth(100)\n  player:teleport(0, 6, 0)\nend)\n\nfunction startRound()\n  local disaster = disasters[math.random(1, #disasters)]\n  print(\"Disaster: \" .. disaster)\n  for _, player in pairs(game.players) do\n    player:teleport(0, 8, -42)\n    game.setScore(player.id, 0)\n  end\nend\n"
        });

        return map;
    }

    private static NovusPart Add(NovusMap map, string name, string type, Vector3 position, Vector3 size, Color color, string material)
    {
        var part = new NovusPart
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Type = type,
            Position = position,
            Size = size,
            Color = color,
            Material = material,
            Anchored = true,
            CanCollide = true
        };
        if (type == "SpawnPoint") part.Material = "Neon";
        map.Objects.Add(part);
        return part;
    }
}
