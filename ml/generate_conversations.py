"""Writes the simulator's conversation library: British English (en-GB) threads between members, per persona.

    uv run generate_conversations.py   # -> src/FeedFabric.Simulator/Data/conversations.json

Seeded, so the same file every time. Three shapes of thread, each a post and its comments in order:
  question    someone asks for help; others give advice; they reply with thanks or a follow-up; someone answers that
  milestone   someone shares a win; others congratulate; they thank one or two back
  discussion  an open question ("morning or evening runs?"); people answer, and some reply to each other
Comments say who writes them: "op" (the poster), "helper" (someone who shares the topic) or "peer" (anyone), and which
earlier comment they reply to, if any. Everything is synthetic and marked so; none of it is real members' words.
"""

import json
import random
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "src/FeedFabric.Simulator/Data/conversations.json"
rnd = random.Random(2026)

# Spoken like people in the UK: mum, trainers, knackered, parkrun, physio, GP, fortnight, cheers, brilliant, chuffed
THANKS = [
    "Cheers, that's really helpful.", "Thanks so much, I'll give that a go.", "Brilliant, thank you!",
    "That makes a lot of sense, thanks.", "Really appreciate this, thank you.", "Ah, I hadn't thought of that. Cheers!",
    "Thank you, feeling a bit less daft now.", "Lovely, I'll try that this week and report back.",
]
FOLLOW_UPS = [
    "Did you find it took long to make a difference?", "How long did you stick with that before it clicked?",
    "Would you do that every day or a few times a week?", "Did you need any kit for that?",
    "Was it a struggle at first or fine straight away?",
]
ANSWERS_TO_FOLLOW_UP = [
    "About a fortnight for me, but stick with it.", "A few times a week was plenty at the start.",
    "Took me a couple of weeks, then it just became normal.", "Honestly no kit, just a bit of patience.",
    "It was hard for the first week, then much easier. You've got this.",
]
CONGRATS = [
    "Brilliant, well done!", "That's massive, you should be really proud.", "Absolutely smashing it!",
    "Love this, congratulations!", "So chuffed for you!", "What an achievement, well done you.",
    "This is the motivation I needed today, well done.", "Fantastic! How are you feeling?",
]
THANKS_FOR_CONGRATS = [
    "Thank you! Honestly couldn't have done it without this group.", "Cheers everyone, still buzzing.",
    "Thank you so much, it means a lot.", "Thanks! Onwards to the next one.", "Ah thank you, very chuffed.",
]
FEELINGS = ["Still can't quite believe it.", "Absolutely knackered but so happy.", "Bit emotional, not going to lie.",
            "Feeling properly proud of myself.", "Grinning like an idiot.", "Took a while but got there."]

P = {
    "runner": {
        "questions": [
            ("My shins are killing me after {n} runs a week.", "Anyone got tips for shin splints?",
             ["Ease off the mileage for a week and build back up slowly, no more than ten per cent a week.",
              "Check your trainers. Mine were worn out and the pain went once I replaced them.",
              "Calf raises and foam rolling every evening sorted mine out.",
              "Try landing a bit softer and shortening your stride. Made a huge difference for me.",
              "If it doesn't settle in a fortnight, see a physio. Worth every penny."]),
            ("I keep going out too fast and dying at {k}k.", "How do you pace a parkrun?",
             ["Start at the back and make yourself run the first km at chat pace.",
              "Run by feel: if you can't speak a full sentence in the first mile, slow down.",
              "Negative splits changed everything for me. First half slower than you think.",
              "A watch with pace alerts helps, but honestly it's practice."]),
            ("Training for my first half and my long runs feel awful.", "Is it normal to hate the long run?",
             ["Totally normal. Slow them right down, slower than feels right.",
              "Take a gel or some jelly babies after an hour. Game changer.",
              "Run with someone else for the long one. Time flies when you're chatting.",
              "Break it into chunks in your head: three lots of four miles feels smaller."]),
        ],
        "milestones": ["Just ran my first sub-{t} 5k at parkrun!", "Ran {k}k without stopping for the first time!",
                       "Signed up for my first half marathon today!", "{w} weeks of marathon training done and still in one piece."],
        "discussions": [("Morning or evening runs? Which do you prefer and why?",
                         ["Mornings, before the day gets in the way.", "Evenings, I'm useless before eight.",
                          "Mornings in summer, evenings in winter. The dark mornings defeat me.",
                          "Lunchtime, if work lets me. Clears my head for the afternoon."])],
    },
    "walker": {
        "questions": [
            ("Trying to get my steps up but I work from home.", "How do you fit more walking into a desk day?",
             ["A walk before work instead of the commute. Twenty minutes round the block.",
              "Take every phone call standing up or walking. Adds up quickly.",
              "I do a lap of the park at lunch, rain or shine. Waterproof jacket is a must.",
              "Walking meetings with colleagues if you can get away with it."]),
            ("My feet ache after long walks.", "Any recommendations for walking shoes?",
             ["Get properly fitted at a running shop, even for walking. They check your gait.",
              "Cushioned trainers rather than fashion ones. My feet thanked me.",
              "Proper socks matter as much as the shoes, honestly.",
              "Break new ones in gradually rather than going straight out for ten miles."]),
        ],
        "milestones": ["Hit {s} steps every day this week!", "Walked the whole coastal path section today, {k} miles!",
                       "{w} weeks of a daily walk and my mood is so much better."],
        "discussions": [("Favourite local walk? Mine's along the canal at sunset.",
                         ["The reservoir loop, especially on a frosty morning.", "Anywhere with a café at the end, honestly.",
                          "Up the hill behind our house. Hard going but the view is worth it.",
                          "The woods near us in autumn. All the colours."])],
    },
    "globetrotter": {
        "questions": [
            ("Off to {place} next month and want to stay active.", "How do you keep healthy habits on holiday?",
             ["Walk everywhere instead of taxis. You see so much more.", "A swim before breakfast every day, sea or pool.",
              "I pack resistance bands. Weigh nothing and do the job.", "Don't stress about it. Enjoy yourself and get back to it after."]),
        ],
        "milestones": ["Back from {place} and I walked about {s} steps a day!", "Did sunrise yoga every morning in {place}. Bliss."],
        "discussions": [("Active holidays or proper rest? I can never decide.",
                         ["Bit of both: active mornings, lazy afternoons.", "Rest. That's what holidays are for!",
                          "Active every time. I feel rubbish if I sit around for a week."])],
    },
    "yogi": {
        "questions": [
            ("My hamstrings are so tight I can barely touch my knees.", "Any tips for getting more flexible?",
             ["Little and often. Ten minutes every day beats an hour once a week.", "Bend your knees in forward folds, there's no shame in it.",
              "Warm up first. A short walk before stretching makes a big difference.", "Breathe into it and don't force anything."]),
            ("Struggling to switch my brain off.", "How do you actually get into meditation?",
             ["Start with three minutes. Seriously, that's enough to begin with.", "A guided app helped me loads at the start.",
              "Focus on counting breaths to ten, then start again. Mind wandering is normal.",
              "Same time every day, so it becomes a habit rather than a decision."]),
        ],
        "milestones": ["{w} weeks of daily yoga and I can finally touch my toes!", "Day {d} of meditating every morning. Calmer than I've been in years."],
        "discussions": [("Morning yoga or evening wind-down? What works for you?",
                         ["Morning, sets me up for the day.", "Evening, helps me sleep.", "Both on a good week!"])],
    },
    "lifter": {
        "questions": [
            ("My squat has been stuck at the same weight for a month.", "How do you get past a plateau?",
             ["Drop the weight by ten per cent and build back up with better form.", "Are you eating enough? Most plateaus are diet.",
              "Change the rep range for a few weeks. Fives instead of eights.", "Film yourself. My depth was nowhere near where I thought."]),
            ("Never lifted before and the free weights area scares me.", "How did you start?",
             ["Book a session with a PT for form. Best money I spent.", "Go at quieter times while you build confidence.",
              "Start with the machines, then move across when you're ready.", "Honestly nobody's watching you. Everyone started somewhere."]),
        ],
        "milestones": ["Hit a {kg}kg deadlift today! Personal best!", "First unassisted pull-up after {w} weeks of trying!"],
        "discussions": [("Full body or split routine? Trying to plan the next block.",
                         ["Full body three times a week if you're newer.", "Push, pull, legs works for me.", "Whatever you'll actually stick to."])],
    },
    "water_wheels": {
        "questions": [
            ("Thinking of trying sea swimming but nervous about the cold.", "How did you get started?",
             ["Go with a group the first few times. Safety first.", "Start in late summer when the water's warmest.",
              "Get in slowly and breathe out long. The gasp passes after a minute.", "Neoprene gloves and socks made it bearable for me."]),
            ("Getting saddle sore on longer rides.", "Any tips for comfort on the bike?",
             ["Get a proper bike fit. Saddle height makes a huge difference.", "Padded shorts and chamois cream. Not glamorous but works.",
              "Build the distance up gradually.", "Stand up on the pedals every twenty minutes or so."]),
        ],
        "milestones": ["Rode {k} miles today, longest ever!", "Swam all through {month} in the sea!"],
        "discussions": [("Pool or open water? I'm torn.", ["Open water every time, nothing beats it.",
                         "Pool for training, sea for fun.", "Pool. I like knowing where the bottom is!"])],
    },
    "habit_builder": {
        "questions": [
            ("Keep starting habits and dropping them after a week.", "How do you make things stick?",
             ["Make it tiny. Two minutes, every day, no exceptions.", "Attach it to something you already do, like after brushing your teeth.",
              "Tick it off on a paper calendar. Don't break the chain.", "Miss once, fine. Never miss twice."]),
            ("Trying to cut back on coffee and the headaches are awful.", "Any advice?",
             ["Cut down gradually, half a cup a week.", "Drink loads of water, it helped my headaches.",
              "Swap the afternoon one for herbal tea first.", "It gets better after about a week, promise."]),
        ],
        "milestones": ["Day {d} of no screens after nine. Sleeping so much better.", "{w} weeks of drinking two litres of water a day!"],
        "discussions": [("What's the one habit that changed things most for you?",
                         ["A proper bedtime.", "Walking after dinner.", "Cooking from scratch on Sundays.", "Leaving my phone in another room."])],
    },
    "recovering": {
        "questions": [
            ("Six weeks after knee surgery and getting impatient.", "How do you stay positive in rehab?",
             ["Write down what you can do each week. Progress is slower than you notice, but it's there.",
              "Do exactly what the physio says, nothing more. I set myself back by rushing.",
              "Find something you can do, like upper body or swimming, so you still feel active.",
              "Patience is the hardest part. You're doing brilliantly getting this far."]),
            ("My lower back flares up whenever I try to get back into exercise.", "Anyone been through this?",
             ["Core work from a physio sorted mine. Boring but it works.", "Walking was the best thing for my back, little and often.",
              "Check with your GP first, then build up really gently.", "Pilates helped me loads, with a teacher who knew about backs."]),
        ],
        "milestones": ["First pain-free run since the injury, {m} minutes!", "Physio signed me off today after {w} weeks!"],
        "discussions": [("What kept you going during a long recovery?", ["Small goals every week.",
                         "This group, honestly.", "Remembering how far I'd come rather than how far to go."])],
    },
    "new_parent": {
        "questions": [
            ("Baby is {mo} months and I've done no exercise since.", "How do you fit anything in?",
             ["Pram walks count! Long ones with a podcast.", "Ten minutes while they nap. Anything is better than nothing.",
              "Buggy fit classes are brilliant, and you meet other parents.", "Be kind to yourself. It's a season, it passes."]),
        ],
        "milestones": ["First run since having the baby. Only {m} minutes but I did it!", "Got {h} hours of sleep in a row. Feel like a new person."],
        "discussions": [("Other parents, what's your quickest workout?", ["Stairs with the baby carrier!",
                         "Squats while rocking them to sleep.", "A fast walk to the shops and back."])],
    },
    "desk_worker": {
        "questions": [
            ("My neck and shoulders are in bits from sitting all day.", "What actually helps?",
             ["Raise your screen to eye level. Made the biggest difference for me.", "Stand up every hour, set a reminder.",
              "Shoulder rolls and chin tucks a few times a day.", "A proper chair, if work will pay for one."]),
        ],
        "milestones": ["Took a proper lunch break every day this week!", "{w} weeks of walking meetings and my back feels loads better."],
        "discussions": [("Standing desk: worth it or a gimmick?", ["Worth it, but don't stand all day either.",
                         "Gimmick for me, I just walk more instead.", "Love mine, I switch every hour."])],
    },
    "silver_stride": {
        "questions": [
            ("Seventy next year and want to stay steady on my feet.", "Any exercises you'd recommend?",
             ["A balance class at the leisure centre. Lovely people too.", "Standing on one leg while the kettle boils.",
              "Walking every day, and some gentle strength work twice a week.", "Tai chi, honestly. Wonderful for balance."]),
        ],
        "milestones": ["Walked {k} miles with the walking group today at {age}!", "Back to bowls after my hip operation!"],
        "discussions": [("What keeps you active as the years go by?", ["My walking group.",
                         "The grandchildren!", "Swimming three mornings a week."])],
    },
    "beginner": {
        "questions": [
            ("Complete beginner and don't know where to start.", "What would you do first?",
             ["Couch to 5k. It's designed for exactly this.", "Just walk every day for a fortnight, then build from there.",
              "Pick something you enjoy, not what you think you should do.", "Start smaller than you think. Consistency beats intensity."]),
            ("Feel self-conscious exercising in public.", "Did anyone else feel like this?",
             ["Everyone did. Nobody's looking, they're all thinking about themselves.", "Go early or late when it's quieter at first.",
              "Headphones in, and it feels like your own little world.", "This group is proof you're not alone."]),
        ],
        "milestones": ["Finished week {w} of couch to 5k!", "Went to my first ever gym class today!"],
        "discussions": [("What made you finally start?", ["A wake-up call from my GP.",
                         "Wanted to keep up with my kids.", "A friend dragged me along and I'm glad she did."])],
    },
}

PLACES = ["Cornwall", "the Lake District", "Lisbon", "Mallorca", "the Peak District", "Edinburgh", "Crete", "Snowdonia", "Norfolk"]
MONTHS = ["January", "February", "October", "November", "December"]


def fill(text: str) -> str:
    return text.format(
        n=rnd.choice(["three", "four", "five"]), k=rnd.choice([3, 5, 8, 10, 15]), t=rnd.choice([25, 28, 30, 35]),
        w=rnd.choice([3, 4, 6, 8, 10, 12]), s=rnd.choice(["10,000", "12,000", "15,000"]), place=rnd.choice(PLACES),
        d=rnd.choice([10, 21, 30, 50, 100]), kg=rnd.choice([80, 100, 120, 140]), month=rnd.choice(MONTHS),
        m=rnd.choice([10, 15, 20, 30]), mo=rnd.choice([3, 4, 6, 9]), h=rnd.choice(["five", "six", "seven"]),
        age=rnd.choice([68, 72, 75, 79]),
    )


def question(persona: str, spec) -> dict:
    context, ask, advice = spec
    post = f"{fill(context)} {ask}"
    replies, helpers = [], rnd.sample(advice, k=rnd.randint(2, min(5, len(advice))))
    for line in helpers:
        replies.append({"who": "helper", "text": line, "parent": None})
    # The poster answers one or two of them: thanks, or a follow-up someone then answers
    for index in rnd.sample(range(len(helpers)), k=min(len(helpers), rnd.randint(1, 2))):
        if rnd.random() < 0.4:
            replies.append({"who": "op", "text": rnd.choice(FOLLOW_UPS), "parent": index})
            replies.append({"who": "helper", "text": rnd.choice(ANSWERS_TO_FOLLOW_UP), "parent": len(replies) - 1})
        else:
            replies.append({"who": "op", "text": rnd.choice(THANKS), "parent": index})
    return {"persona": persona, "kind": "question", "post": post, "replies": replies}


def milestone(persona: str, line: str) -> dict:
    post = f"{fill(line)} {rnd.choice(FEELINGS)}"
    replies = [{"who": "peer", "text": t, "parent": None} for t in rnd.sample(CONGRATS, k=rnd.randint(2, 6))]
    for index in rnd.sample(range(len(replies)), k=rnd.randint(1, 2)):
        replies.append({"who": "op", "text": rnd.choice(THANKS_FOR_CONGRATS), "parent": index})
    return {"persona": persona, "kind": "milestone", "post": post, "replies": replies}


def discussion(persona: str, spec) -> dict:
    post, answers = spec
    replies = [{"who": "peer", "text": t, "parent": None} for t in rnd.sample(answers, k=rnd.randint(2, len(answers)))]
    if rnd.random() < 0.6:
        replies.append({"who": "peer", "text": rnd.choice(["Same here!", "Ha, this is so true.", "Completely agree.",
                                                         "Never thought of it that way."]), "parent": rnd.randrange(len(replies))})
    return {"persona": persona, "kind": "discussion", "post": post, "replies": replies}


def main() -> None:
    threads, seen = [], set()
    for persona, spec in P.items():
        made = 0
        while made < 160:
            roll = rnd.random()
            thread = (question(persona, rnd.choice(spec["questions"])) if roll < 0.5
                      else milestone(persona, rnd.choice(spec["milestones"])) if roll < 0.85
                      else discussion(persona, rnd.choice(spec["discussions"])))
            key = (thread["post"], tuple(r["text"] for r in thread["replies"]))
            if key in seen:
                continue
            seen.add(key)
            threads.append(thread)
            made += 1
    rnd.shuffle(threads)
    OUT.write_text(json.dumps({"locale": "en-GB", "synthetic": True, "threads": threads}, ensure_ascii=False, indent=1))
    kinds = {k: sum(t["kind"] == k for t in threads) for k in ("question", "milestone", "discussion")}
    print(f"{len(threads)} threads, {sum(len(t['replies']) for t in threads)} comments -> {OUT.relative_to(OUT.parents[2])} {kinds}")


if __name__ == "__main__":
    main()
