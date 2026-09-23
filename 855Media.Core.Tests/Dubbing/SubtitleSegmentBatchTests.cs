using System;
using System.Collections.Generic;
using System.Text.Json;
using _855Media.Core.Dubbing;
using Xunit;

namespace _855Media.Core.Tests.Dubbing;

public class SubtitleSegmentBatchTests
{
    [Fact]
    public void SubtitleSegment_IsSelected_PropertyChangeNotified()
    {
        var seg = new SubtitleSegment();
        var changedProps = new List<string>();
        seg.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
                changedProps.Add(e.PropertyName);
        };

        seg.IsSelected = true;

        Assert.True(seg.IsSelected);
        Assert.Contains(nameof(SubtitleSegment.IsSelected), changedProps);
    }

    [Fact]
    public void SubtitleSegment_AssignedCharacter_SyncsSpeakerProperties()
    {
        var seg = new SubtitleSegment();
        var character = new MovieCharacter
        {
            Id = Guid.NewGuid(),
            Name = "Sophea (Queen)",
            ColorTag = "#EC4899",
            BaseVoice = "km-KH-SreymomNeural",
        };

        var changedProps = new List<string>();
        seg.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
                changedProps.Add(e.PropertyName);
        };

        seg.AssignedCharacter = character;

        Assert.Equal(character, seg.AssignedCharacter);
        Assert.Equal(character.Id, seg.CharacterId);
        Assert.Equal("Sophea (Queen)", seg.SpeakerName);
        Assert.Equal("#EC4899", seg.SpeakerColor);

        Assert.Contains(nameof(SubtitleSegment.AssignedCharacter), changedProps);
        Assert.Contains(nameof(SubtitleSegment.CharacterId), changedProps);
        Assert.Contains(nameof(SubtitleSegment.SpeakerName), changedProps);
        Assert.Contains(nameof(SubtitleSegment.SpeakerColor), changedProps);
    }

    [Fact]
    public void SubtitleSegment_AssignedEmotionConfig_SyncsEmotionProperties()
    {
        var seg = new SubtitleSegment { Emotion = ActorEmotionEngine.EmotionNormal };
        var cryingConfig = ActorEmotionEngine.GetConfig(ActorEmotionEngine.EmotionCrying);

        var changedProps = new List<string>();
        seg.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
                changedProps.Add(e.PropertyName);
        };

        seg.AssignedEmotionConfig = cryingConfig;

        Assert.Equal(ActorEmotionEngine.EmotionCrying, seg.Emotion);
        Assert.Equal(cryingConfig.DisplayName, seg.EmotionDisplayName);
        Assert.Equal(cryingConfig.Color, seg.EmotionColor);
        Assert.Equal(cryingConfig.Icon, seg.EmotionIcon);

        Assert.Contains(nameof(SubtitleSegment.Emotion), changedProps);
        Assert.Contains(nameof(SubtitleSegment.AssignedEmotionConfig), changedProps);
    }

    [Fact]
    public void SubtitleSegment_JsonSerialization_IgnoresComplexNavProperties()
    {
        var seg = new SubtitleSegment
        {
            Index = 1,
            OriginalText = "Hello",
            KhmerText = "សួស្តី",
            StartTime = TimeSpan.FromSeconds(1),
            EndTime = TimeSpan.FromSeconds(3),
            IsSelected = true,
            Emotion = ActorEmotionEngine.EmotionAngry,
            AssignedCharacter = new MovieCharacter { Name = "General", ColorTag = "#EF4444" },
        };

        var json = JsonSerializer.Serialize(seg);

        // AssignedCharacter and AssignedEmotionConfig should not be serialized
        Assert.DoesNotContain("\"AssignedCharacter\"", json);
        Assert.DoesNotContain("\"AssignedEmotionConfig\"", json);

        // Serialized fields must be present
        Assert.Contains("\"IsSelected\":true", json);
        Assert.Contains("\"Emotion\":\"Angry\"", json);
        Assert.Contains("\"SpeakerName\":\"General\"", json);
        Assert.Contains("\"SpeakerColor\":\"#EF4444\"", json);

        var deserialized = JsonSerializer.Deserialize<SubtitleSegment>(json);
        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsSelected);
        Assert.Equal("Angry", deserialized.Emotion);
        Assert.Equal("General", deserialized.SpeakerName);
        Assert.Equal("#EF4444", deserialized.SpeakerColor);
    }

    [Fact]
    public void MovieCharacter_Equality_MatchesByIdAndName()
    {
        var id = Guid.NewGuid();
        var char1 = new MovieCharacter { Id = id, Name = "Hero (Male)" };
        var char2 = new MovieCharacter { Id = id, Name = "Hero (Male)" };
        var char3 = new MovieCharacter { Id = Guid.NewGuid(), Name = "Hero (Male)" };
        var char4 = new MovieCharacter { Id = Guid.NewGuid(), Name = "Heroine (Female)" };

        Assert.Equal(char1, char2);
        Assert.Equal(char1, char3); // Match by name even if distinct Guid instances
        Assert.NotEqual(char1, char4);
        Assert.True(char1.Equals((object)char3));
        Assert.Equal(char1.GetHashCode(), char3.GetHashCode());
    }

    [Fact]
    public void SubtitleSegment_CharacterResolver_AutoResolvesWhenSpeakerChanges()
    {
        var male = new MovieCharacter { Id = Guid.NewGuid(), Name = "Hero (Male)" };
        var female = new MovieCharacter { Id = Guid.NewGuid(), Name = "Heroine (Female)" };
        var cast = new List<MovieCharacter> { male, female };

        SubtitleSegment.CharacterResolver = (id, name) =>
        {
            if (id != null)
            {
                var byId = cast.Find(c => c.Id == id.Value);
                if (byId != null)
                    return byId;
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                var byName = cast.Find(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                );
                if (byName != null)
                    return byName;
            }
            return cast[0];
        };

        var seg = new SubtitleSegment { CharacterId = male.Id, SpeakerName = male.Name };
        Assert.Same(male, seg.AssignedCharacter);

        var changedProps = new List<string>();
        seg.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
                changedProps.Add(e.PropertyName);
        };

        // Simulate Auto Voice assigning female character
        seg.AssignedCharacter = female;

        Assert.Same(female, seg.AssignedCharacter);
        Assert.Equal(female.Id, seg.CharacterId);
        Assert.Equal("Heroine (Female)", seg.SpeakerName);
        Assert.Contains(nameof(SubtitleSegment.AssignedCharacter), changedProps);

        // Simulate external CharacterId change
        changedProps.Clear();
        seg.CharacterId = male.Id;
        Assert.Same(male, seg.AssignedCharacter);
        Assert.Contains(nameof(SubtitleSegment.AssignedCharacter), changedProps);
    }
}
