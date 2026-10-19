using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace _855Media.Core.LDPlayer;

public sealed class ScreenTimeOptions : INotifyPropertyChanged
{
    private bool _randomFeedReactions = false;
    public bool RandomFeedReactions
    {
        get => _randomFeedReactions;
        set
        {
            if (!Equals(_randomFeedReactions, value))
            {
                _randomFeedReactions = value;
                Changed();
            }
        }
    }
    private bool _randomFeedComments = false;
    public bool RandomFeedComments
    {
        get => _randomFeedComments;
        set
        {
            if (!Equals(_randomFeedComments, value))
            {
                _randomFeedComments = value;
                Changed();
            }
        }
    }
    private bool _reviewEachRandomComment = true;
    public bool ReviewEachRandomComment
    {
        get => _reviewEachRandomComment;
        set
        {
            if (!Equals(_reviewEachRandomComment, value))
            {
                _reviewEachRandomComment = value;
                Changed();
            }
        }
    }
    private string _randomCommentTexts = "";
    public string RandomCommentTexts
    {
        get => _randomCommentTexts;
        set
        {
            if (!Equals(_randomCommentTexts, value))
            {
                _randomCommentTexts = value;
                Changed();
            }
        }
    }
    private int _interactionChancePercent = 25;
    public int InteractionChancePercent
    {
        get => _interactionChancePercent;
        set
        {
            if (!Equals(_interactionChancePercent, value))
            {
                _interactionChancePercent = value;
                Changed();
            }
        }
    }
    private bool _stories = false;
    public bool Stories
    {
        get => _stories;
        set
        {
            if (!Equals(_stories, value))
            {
                _stories = value;
                Changed();
            }
        }
    }
    private int _storyLimit = 5;
    public int StoryLimit
    {
        get => _storyLimit;
        set
        {
            if (!Equals(_storyLimit, value))
            {
                _storyLimit = value;
                Changed();
            }
        }
    }
    private int _storyDwellSeconds = 5;
    public int StoryDwellSeconds
    {
        get => _storyDwellSeconds;
        set
        {
            if (!Equals(_storyDwellSeconds, value))
            {
                _storyDwellSeconds = value;
                Changed();
            }
        }
    }
    private int _maxSessionSeconds = 1800;
    public int MaxSessionSeconds
    {
        get => _maxSessionSeconds;
        set
        {
            if (!Equals(_maxSessionSeconds, value))
            {
                _maxSessionSeconds = value;
                Changed();
            }
        }
    }
    private int _feedDwellSeconds = 3;
    public int FeedDwellSeconds
    {
        get => _feedDwellSeconds;
        set
        {
            if (!Equals(_feedDwellSeconds, value))
            {
                _feedDwellSeconds = value;
                Changed();
            }
        }
    }
    private int _reelDwellMinSeconds = 8;
    public int ReelDwellMinSeconds
    {
        get => _reelDwellMinSeconds;
        set
        {
            if (_reelDwellMinSeconds != value)
            {
                _reelDwellMinSeconds = value;
                Changed();
            }
        }
    }
    private int _reelDwellMaxSeconds = 30;
    public int ReelDwellMaxSeconds
    {
        get => _reelDwellMaxSeconds;
        set
        {
            if (_reelDwellMaxSeconds != value)
            {
                _reelDwellMaxSeconds = value;
                Changed();
            }
        }
    }

    // Compatibility for callers assigning a fixed duration; old saved fixed values
    // are intentionally superseded by the new default range.
    [System.Text.Json.Serialization.JsonIgnore]
    public int ReelDwellSeconds
    {
        get => ReelDwellMinSeconds;
        set
        {
            ReelDwellMinSeconds = value;
            ReelDwellMaxSeconds = value;
        }
    }
    private bool _reactions = false;
    public bool Reactions
    {
        get => _reactions;
        set
        {
            if (!Equals(_reactions, value))
            {
                _reactions = value;
                Changed();
            }
        }
    }
    private string _reaction = "Like";
    public string Reaction
    {
        get => _reaction;
        set
        {
            if (!Equals(_reaction, value))
            {
                _reaction = value;
                Changed();
            }
        }
    }
    private int _reactionLimit = 3;
    public int ReactionLimit
    {
        get => _reactionLimit;
        set
        {
            if (!Equals(_reactionLimit, value))
            {
                _reactionLimit = value;
                Changed();
            }
        }
    }
    private bool _comments = false;
    public bool Comments
    {
        get => _comments;
        set
        {
            if (!Equals(_comments, value))
            {
                _comments = value;
                Changed();
            }
        }
    }
    private int _commentLimit = 1;
    public int CommentLimit
    {
        get => _commentLimit;
        set
        {
            if (!Equals(_commentLimit, value))
            {
                _commentLimit = value;
                Changed();
            }
        }
    }
    private int _reviewTimeoutSeconds = 120;
    public int ReviewTimeoutSeconds
    {
        get => _reviewTimeoutSeconds;
        set
        {
            if (!Equals(_reviewTimeoutSeconds, value))
            {
                _reviewTimeoutSeconds = value;
                Changed();
            }
        }
    }
    private ObservableCollection<ScreenTimeCommentDraft> _drafts = [];
    private readonly System.Collections.Generic.HashSet<ScreenTimeCommentDraft> _observedDrafts =
    [];

    public ScreenTimeOptions()
    {
        _drafts.CollectionChanged += DraftCollectionChanged;
    }

    public ObservableCollection<ScreenTimeCommentDraft> Drafts
    {
        get => _drafts;
        set
        {
            _drafts.CollectionChanged -= DraftCollectionChanged;
            foreach (var d in _observedDrafts)
                d.PropertyChanged -= DraftChanged;
            _observedDrafts.Clear();
            _drafts = value ?? [];
            _drafts.CollectionChanged += DraftCollectionChanged;
            ObserveDrafts();
            Changed();
        }
    }

    private void DraftCollectionChanged(
        object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e
    )
    {
        ObserveDrafts();
        Changed(nameof(Drafts));
    }

    private void ObserveDrafts()
    {
        foreach (var d in System.Linq.Enumerable.ToArray(_observedDrafts))
        {
            if (!_drafts.Contains(d))
            {
                d.PropertyChanged -= DraftChanged;
                _observedDrafts.Remove(d);
            }
        }
        foreach (var d in _drafts)
        {
            if (_observedDrafts.Add(d))
                d.PropertyChanged += DraftChanged;
        }
    }

    private void DraftChanged(object? sender, PropertyChangedEventArgs e) =>
        Changed(nameof(Drafts));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new(name));

    public ScreenTimeOptions Snapshot() =>
        JsonSerializer.Deserialize<ScreenTimeOptions>(JsonSerializer.Serialize(this))!;

    public static string[] ReactionTypes { get; } =
    ["Like", "Love", "Care", "Haha", "Wow", "Sad", "Angry"];
}

public sealed class ScreenTimeCommentDraft : INotifyPropertyChanged
{
    private string _postUrl = "";
    private string _text = "";
    public string PostUrl
    {
        get => _postUrl;
        set
        {
            _postUrl = value;
            PropertyChanged?.Invoke(this, new(nameof(PostUrl)));
        }
    }
    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            PropertyChanged?.Invoke(this, new(nameof(Text)));
        }
    }
    public string PostId => ScreenTimeHierarchy.PostId(PostUrl) ?? "";
    public event PropertyChangedEventHandler? PropertyChanged;
}
