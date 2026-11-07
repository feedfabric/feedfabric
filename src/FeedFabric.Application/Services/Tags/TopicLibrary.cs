namespace FeedFabric.Application.Services.Tags;

// A ready-made set of topics for one kind of community, to start from rather than write from nothing
public sealed record TopicLibrarySet(string Key, string Name, string Description, IReadOnlyDictionary<string, string[]> Keywords);

// The sets staff can choose from in the admin panel. Wellness is what a server uses until someone chooses.
public static class TopicLibrary
{
    public const string Wellness = "wellness";

    public static readonly IReadOnlyList<TopicLibrarySet> All =
    [
        new(Wellness, "Wellness", "Everyday movement, rest and looking after yourself", new Dictionary<string, string[]>
        {
            ["running"] = ["run", "ran", "running", "jog", "5k", "10k", "parkrun", "marathon", "/km", "pace", "intervals", "tempo"],
            ["walking"] = ["walk", "walked", "walking", "stroll", "steps", "hike", "hiked", "hiking", "pram walk"],
            ["travel"] = ["holiday", "trip", "travel", "back from", "beach", "villages", "balcony", "waterfall", "lisbon", "crete", "lake district",
                "cornwall", "lanzarote", "tuscany", "sri lanka", "edinburgh", "algarve", "bali", "iceland", "mallorca", "peak district", "kyoto",
                "snowdonia", "barcelona", "dolomites", "norfolk", "costa rica", "santorini", "vienna", "dorset"],
            ["yoga"] = ["yoga", "pilates", "flow", "pose", "savasana", "sun salutations", "stretch", "stretching"],
            ["strength"] = ["gym", "squats", "deadlifts", "bench", "overhead press", "pull-ups", "push day", "leg day", "kettlebell", "rep", "lifting",
                "strength", "protein"],
            ["cycling"] = ["bike", "ride", "rode", "cycle", "cycled", "cycling", "saddle", "hill ride"],
            ["swimming"] = ["swim", "swam", "swimming", "lido", "open water", "sea swim", "snorkelled", "cold water"],
            ["sleep"] = ["sleep", "slept", "bedtime", "early night", "hours of sleep", "nap"],
            ["nutrition"] = ["coffee", "drinking more water", "meal prep", "cooking", "breakfast", "fruit", "snack", "alcohol", "herbal tea"],
            ["mindfulness"] = ["meditation", "breathwork", "journaling", "body scan", "no screens", "phone-free", "switched off", "calm"],
            ["recovery"] = ["physio", "rehab", "injury", "sore", "my knee", "my lower back", "my ankle", "my hips", "rest day", "recovery"],
            ["parenting"] = ["baby", "kids", "pram", "buggy", "nap times", "nap time"],
            ["workday"] = ["desk", "meeting", "lunch break", "taking the stairs", "took the stairs", "3pm", "standing"],
            ["beginner"] = ["first ever", "couch to 5k", "beginner", "i'm new", "bought proper trainers", "scared of the gym"],
        }),
        new("climbing", "Climbing", "Bouldering, sport, trad and the training and travel around them", new Dictionary<string, string[]>
        {
            ["bouldering"] = ["boulder", "bouldering", "bouldered", "crash pad", "highball", "mantle", "top out", "topped out"],
            ["sport"] = ["sport climbing", "quickdraws", "clipped", "lead climb", "lead climbing", "redpoint", "redpointed", "onsight", "onsighted",
                "flash", "flashed"],
            ["trad"] = ["trad", "cams", "nuts", "gear placement", "placed gear", "multi-pitch", "multipitch", "belay ledge", "abseil", "rack"],
            ["indoor"] = ["climbing wall", "the wall", "bouldering gym", "climbing gym", "reset", "new set", "auto belay", "top rope", "top roping"],
            ["outdoor"] = ["crag", "the crag", "grit", "gritstone", "limestone", "granite", "approach", "guidebook", "fontainebleau", "stanage",
                "the peak", "kalymnos", "el cap", "yosemite"],
            ["training"] = ["hangboard", "fingerboard", "campus board", "deadhang", "deadhangs", "moonboard", "kilter", "board session", "pull-ups"],
            ["projects"] = ["project", "projecting", "send", "sent it", "the send", "crux", "beta", "high point", "one hang", "first ascent"],
            ["injury"] = ["pulley", "finger injury", "tendon", "elbow", "shoulder", "physio", "rest day", "taped", "split tip", "flapper"],
            ["gear"] = ["shoes", "harness", "rope", "chalk", "chalk bag", "belay device", "helmet", "resole", "resoled"],
            ["safety"] = ["belay", "belayer", "belaying", "took a fall", "whipper", "lowered off", "anchor", "bolts", "rockfall"],
            ["beginner"] = ["first time climbing", "beginner", "i'm new", "taster session", "intro session", "first lead", "learning to lead"],
        }),
        new("running", "Running", "Training, races and the people who run them", new Dictionary<string, string[]>
        {
            ["races"] = ["race", "raced", "race day", "start line", "finish line", "pb", "pr", "medal", "bib", "chip time", "marathon",
                "half marathon", "10k", "5k"],
            ["parkrun"] = ["parkrun", "junior parkrun", "volunteered", "tail walker", "barcode"],
            ["training"] = ["intervals", "tempo", "long run", "easy run", "strides", "fartlek", "hill reps", "track session", "threshold",
                "training plan", "taper", "tapering"],
            ["trail"] = ["trail", "trails", "fell", "fell running", "ultra", "ultramarathon", "elevation", "descent", "mud", "bog"],
            ["gear"] = ["trainers", "shoes", "carbon plate", "watch", "garmin", "strava", "vest", "head torch", "gels"],
            ["injury"] = ["shin splints", "it band", "plantar", "achilles", "calf", "hamstring", "physio", "foam roller", "niggle"],
            ["nutrition"] = ["fuelling", "carb loading", "gels", "electrolytes", "hydration", "breakfast before"],
            ["beginner"] = ["couch to 5k", "c25k", "first run", "first ever", "beginner", "i'm new", "run walk"],
            ["clubs"] = ["running club", "club run", "group run", "running buddy", "club night"],
        }),
        new("cycling", "Cycling", "Road, gravel, mountain biking and getting about by bike", new Dictionary<string, string[]>
        {
            ["road"] = ["road bike", "sportive", "chaingang", "peloton", "drafting", "climb", "col", "descent", "kom"],
            ["gravel"] = ["gravel", "gravel bike", "bikepacking", "bridleway", "byway", "tubeless"],
            ["mtb"] = ["mtb", "mountain bike", "trail centre", "singletrack", "berms", "drop", "jumps", "enduro", "downhill", "uplift"],
            ["commuting"] = ["commute", "commuting", "cycle lane", "bike lane", "panniers", "cycle to work", "traffic"],
            ["training"] = ["zwift", "turbo", "ftp", "intervals", "watts", "power meter", "base miles", "sweet spot"],
            ["maintenance"] = ["puncture", "chain", "gears", "indexing", "brake pads", "bottom bracket", "service", "bike shop", "lbs"],
            ["touring"] = ["tour", "touring", "audax", "randonneur", "lands end", "c2c", "coast to coast", "lejog"],
            ["gear"] = ["helmet", "lights", "bib shorts", "jersey", "cleats", "clipless", "garmin", "wahoo", "strava"],
            ["safety"] = ["close pass", "near miss", "dashcam", "hi-vis", "crash", "came off"],
        }),
        new("gardening", "Gardening", "Growing food and flowers, allotments and houseplants", new Dictionary<string, string[]>
        {
            ["vegetables"] = ["veg", "vegetables", "tomatoes", "courgettes", "potatoes", "runner beans", "onions", "garlic", "harvest", "harvested"],
            ["allotment"] = ["allotment", "plot", "raised bed", "raised beds", "polytunnel", "greenhouse", "cold frame"],
            ["flowers"] = ["flowers", "roses", "dahlias", "tulips", "bulbs", "bloom", "blooming", "border", "cut flowers"],
            ["houseplants"] = ["houseplant", "houseplants", "monstera", "pothos", "repotted", "repotting", "propagating", "cutting"],
            ["seeds"] = ["seeds", "sowing", "sowed", "seedlings", "germinated", "pricked out", "potting on", "hardening off"],
            ["soil"] = ["compost", "composting", "mulch", "manure", "soil", "no dig", "leaf mould"],
            ["pests"] = ["slugs", "snails", "aphids", "blight", "pests", "caterpillars", "netting"],
            ["wildlife"] = ["pollinators", "bees", "butterflies", "hedgehog", "pond", "bird feeder", "wildflower"],
            ["seasons"] = ["frost", "first frost", "spring", "autumn jobs", "overwintering", "heatwave", "watering"],
        }),
    ];

    public static TopicLibrarySet? Find(string key) => All.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static TopicLibrarySet Default => All[0];
}
