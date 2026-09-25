using System.Collections.Generic;

namespace _855Media.Localization;

public partial class LocalizationManager
{
    private static readonly IReadOnlyDictionary<string, string> KhmerLocalization = new Dictionary<
        string,
        string
    >
    {
        // Dashboard
        [nameof(QueryWatermark)] = "URL ឬពាក្យស្វែងរក",
        [nameof(QueryTooltip)] =
            "ទទួលយក URL ឬ ID YouTube ត្រឹមត្រូវណាមួយ។ ដាក់សញ្ញាសួរ (?) នៅខាងមុខ ដើម្បីស្វែងរកតាមអត្ថបទ។",
        [nameof(ProcessQueryTooltip)] = "ដំណើរការពាក្យស្វែងរក (Enter)",
        [nameof(AuthTooltip)] = "ការផ្ទៀងផ្ទាត់",
        [nameof(SettingsTooltip)] = "ការកំណត់",
        [nameof(DownloaderTabTitle)] = "ទាញយក",
        [nameof(UpscalerTabTitle)] = "កែសម្រួល",
        [nameof(DashboardPromptTitle)] = "តើអ្នកចង់ទាញយកអ្វី?",
        [nameof(DashboardPlaceholder)] = """
            ចម្លងបិទភ្ជាប់ **URL** ឬបញ្ចូល **ពាក្យស្វែងរក** ដើម្បីចាប់ផ្តើមទាញយក
            ចុច **Shift+Enter** ដើម្បីបន្ថែមធាតុច្រើន
            """,
        [nameof(DownloadsFileColumnHeader)] = "ឯកសារ",
        [nameof(DownloadsStatusColumnHeader)] = "ស្ថានភាព",
        [nameof(ContextMenuRemoveSuccessful)] = "យកការទាញយកដែលជោគជ័យចេញ",
        [nameof(ContextMenuRemoveInactive)] = "យកការទាញយកដែលមិនសកម្មចេញ",
        [nameof(ContextMenuRestartFailed)] = "ចាប់ផ្តើមការទាញយកដែលបរាជ័យឡើងវិញ",
        [nameof(ContextMenuCancelAll)] = "បោះបង់ការទាញយកទាំងអស់",
        [nameof(DownloadStatusEnqueued)] = "កំពុងរង់ចាំ...",
        [nameof(DownloadStatusCompleted)] = "រួចរាល់",
        [nameof(DownloadStatusCanceled)] = "បានបោះបង់",
        [nameof(DownloadStatusFailed)] = "បរាជ័យ",
        [nameof(ClickToCopyErrorTooltip)] = "ចំណាំ៖ ចុចដើម្បីចម្លងសារកំហុសនេះ",
        [nameof(ShowFileTooltip)] = "បង្ហាញឯកសារ",
        [nameof(PlayTooltip)] = "ចាក់",
        [nameof(CancelDownloadTooltip)] = "បោះបង់ការទាញយក",
        [nameof(RestartDownloadTooltip)] = "ចាប់ផ្តើមទាញយកឡើងវិញ",
        // Settings
        [nameof(SettingsTitle)] = "ការកំណត់",
        [nameof(ThemeLabel)] = "រចនាប័ទ្ម",
        [nameof(ThemeTooltip)] = "រចនាប័ទ្មចំណុចប្រទាក់ដែលចង់បាន",
        [nameof(LanguageLabel)] = "ភាសា",
        [nameof(LanguageTooltip)] = "ភាសាបង្ហាញដែលចង់បានសម្រាប់ចំណុចប្រទាក់អ្នកប្រើ",
        [nameof(AutoUpdateLabel)] = "ធ្វើបច្ចុប្បន្នភាពស្វ័យប្រវត្តិ",
        [nameof(AutoUpdateTooltip)] = """
            ធ្វើបច្ចុប្បន្នភាពស្វ័យប្រវត្តិនៅពេលបើកកម្មវិធីរាល់ដង។
            **ការព្រមាន៖** យើងណែនាំឱ្យបើកជម្រើសនេះ ដើម្បីធានាថាកម្មវិធីត្រូវគ្នាជាមួយកំណែ YouTube ថ្មីបំផុត។
            """,
        [nameof(PersistAuthLabel)] = "រក្សាទុកការផ្ទៀងផ្ទាត់",
        [nameof(PersistAuthTooltip)] = """
            រក្សាទុកខូគីផ្ទៀងផ្ទាត់ទៅក្នុងឯកសារ ដើម្បីអាចបន្តប្រើបានរវាងសម័យប្រើប្រាស់។
            **ការព្រមាន**៖ ទោះបីខូគីត្រូវបានរក្សាទុកជាមួយការអ៊ិនគ្រីបក៏ដោយ វាអាចនៅតែត្រូវបានសង្គ្រោះដោយអ្នកវាយប្រហារដែលមានសិទ្ធិចូលប្រព័ន្ធរបស់អ្នក។
            """,
        [nameof(InjectAltLanguagesLabel)] = "បញ្ចូលភាសាជំនួស",
        [nameof(InjectAltLanguagesTooltip)] =
            "បញ្ចូលបទសំឡេងជាភាសាជំនួស (បើមាន) ទៅក្នុងឯកសារដែលបានទាញយក",
        [nameof(InjectSubtitlesLabel)] = "បញ្ចូលចំណងជើងរង",
        [nameof(InjectSubtitlesTooltip)] = "បញ្ចូលចំណងជើងរង (បើមាន) ទៅក្នុងឯកសារដែលបានទាញយក",
        [nameof(TranslateCaptionsToEnglishLabel)] = "បកប្រែចំណងជើងរងជាភាសាអង់គ្លេស",
        [nameof(TranslateCaptionsToEnglishTooltip)] =
            "បកប្រែចំណងជើងរងទៅជាភាសាអង់គ្លេសដោយស្វ័យប្រវត្តិ និងបញ្ចូលទៅក្នុងវីដេអូ",
        [nameof(InjectTagsLabel)] = "បញ្ចូលស្លាកមេឌៀ",
        [nameof(InjectTagsTooltip)] = "បញ្ចូលស្លាកមេឌៀ (បើមាន) ទៅក្នុងឯកសារដែលបានទាញយក",
        [nameof(SaveTitleToTextFileLabel)] = "រក្សាទុកចំណងជើងទៅជាឯកសារអត្ថបទ",
        [nameof(SaveTitleToTextFileTooltip)] =
            "រក្សាទុកចំណងជើងវីដេអូពេញលេញក្នុងឯកសារ .txt នៅជិតឯកសារដែលបានទាញយក",
        [nameof(TranslateTitleToEnglishLabel)] = "បកប្រែចំណងជើងនិង Caption ទៅជាភាសាអង់គ្លេស",
        [nameof(TranslateTitleToEnglishTooltip)] =
            "បកប្រែចំណងជើងវីដេអូ និង Caption ទៅជាភាសាអង់គ្លេសសម្រាប់ឈ្មោះឯកសារ និងឯកសារអត្ថបទ",
        [nameof(SkipExistingFilesLabel)] = "រំលងឯកសារដែលមានរួច",
        [nameof(SkipExistingFilesTooltip)] =
            "ពេលទាញយកវីដេអូច្រើន រំលងវីដេអូដែលមានឯកសារត្រូវគ្នានៅក្នុងថតលទ្ធផលរួចហើយ",
        [nameof(FileNameTemplateLabel)] = "គំរូឈ្មោះឯកសារ",
        [nameof(FileNameTemplateTooltip)] = """
            គំរូដែលប្រើសម្រាប់បង្កើតឈ្មោះឯកសារសម្រាប់វីដេអូដែលបានទាញយក។

            ថូខឹនដែលអាចប្រើបាន៖
            **$num** - លំដាប់វីដេអូក្នុងបញ្ជី (បើមាន)
            **$id** - ID វីដេអូ
            **$title** - ចំណងជើងវីដេអូ
            **$author** - អ្នកបង្កើតវីដេអូ
            """,
        [nameof(ParallelLimitLabel)] = "ដែនកំណត់ដំណាលគ្នា",
        [nameof(ParallelLimitTooltip)] = "ចំនួនការទាញយកដែលអាចសកម្មក្នុងពេលតែមួយ",
        [nameof(FFmpegPathLabel)] = "ទីតាំង FFmpeg",
        [nameof(FFmpegPathTooltip)] =
            "ទីតាំងឯកសារដំណើរការ FFmpeg។ ទុកទទេដើម្បីប្រើការរកឃើញស្វ័យប្រវត្តិ។",
        [nameof(FFmpegPathWatermark)] = "រកឃើញស្វ័យប្រវត្តិ",
        [nameof(FFmpegPathResetTooltip)] = "កំណត់ឡើងវិញទៅការរកឃើញស្វ័យប្រវត្តិ",
        [nameof(FFmpegPathBrowseTooltip)] = "រកមើលឯកសារដំណើរការ FFmpeg",
        // Auth Setup
        [nameof(AuthenticationTitle)] = "ការផ្ទៀងផ្ទាត់",
        [nameof(AuthenticatedText)] = "អ្នកកំពុងបានផ្ទៀងផ្ទាត់",
        [nameof(LogOutButton)] = "ចាកចេញ",
        [nameof(LoadingText)] = "កំពុងផ្ទុក...",
        // Download Single Setup
        [nameof(CopyMenuItem)] = "ចម្លង",
        [nameof(LiveLabel)] = "ផ្សាយផ្ទាល់",
        [nameof(AudioLabel)] = "សំឡេង",
        [nameof(UpscaledLabel)] = "បានបង្កើនគុណភាព",
        [nameof(FormatLabel)] = "ទ្រង់ទ្រាយ",
        // Download Multiple Setup
        [nameof(VideoTypeLabel)] = "ប្រភេទវីដេអូ",
        [nameof(ListViewTooltip)] = "មើលជាបញ្ជី",
        [nameof(GridViewTooltip)] = "មើលជាក្រឡា",
        [nameof(ContainerLabel)] = "កុងតឺន័រ",
        [nameof(VideoQualityLabel)] = "គុណភាពវីដេអូ",
        // Common buttons
        [nameof(CloseButton)] = "បិទ",
        [nameof(DownloadButton)] = "ទាញយក",
        [nameof(CancelButton)] = "បោះបង់",
        // Dialog messages
        [nameof(UkraineSupportTitle)] = "សូមអរគុណសម្រាប់ការគាំទ្រអ៊ុយក្រែន!",
        [nameof(UkraineSupportMessage)] = """
            ខណៈដែលរុស្ស៊ីកំពុងធ្វើសង្គ្រាមប្រល័យពូជសាសន៍ប្រឆាំងនឹងប្រទេសរបស់ខ្ញុំ ខ្ញុំសូមអរគុណដល់អ្នកទាំងអស់ដែលបន្តឈរជាមួយអ៊ុយក្រែនក្នុងការប្រយុទ្ធដើម្បីសេរីភាព។

            ចុច ស្វែងយល់បន្ថែម ដើម្បីរកវិធីដែលអ្នកអាចជួយបាន។
            """,
        [nameof(LearnMoreButton)] = "ស្វែងយល់បន្ថែម",
        [nameof(UnstableBuildTitle)] = "ការព្រមានអំពីកំណែមិនស្ថិតស្ថេរ",
        [nameof(UnstableBuildMessage)] = """
            អ្នកកំពុងប្រើកំណែអភិវឌ្ឍន៍របស់ {0}។ កំណែទាំងនេះមិនបានសាកល្បងយ៉ាងពេញលេញទេ ហើយអាចមានកំហុស។

            ការធ្វើបច្ចុប្បន្នភាពស្វ័យប្រវត្តិត្រូវបានបិទសម្រាប់កំណែអភិវឌ្ឍន៍។

            ចុច មើលកំណែចេញផ្សាយ ប្រសិនបើអ្នកចង់ទាញយកកំណែស្ថិតស្ថេរជំនួស។
            """,
        [nameof(SeeReleasesButton)] = "មើលកំណែចេញផ្សាយ",
        [nameof(FFmpegMissingTitle)] = "បាត់ FFmpeg",
        [nameof(FFmpegMissingMessage)] =
            "រកមិនឃើញ FFmpeg នៅលើប្រព័ន្ធរបស់អ្នកទេ។ វាចាំបាច់សម្រាប់ឱ្យ {0} ដំណើរការ។ តើអ្នកចង់ទាញយកវាឥឡូវនេះទេ?",
        [nameof(FFmpegDownloadingTitle)] = "កំពុងទាញយក FFmpeg...",
        [nameof(FFmpegDownloadCompletedTitle)] = "បានទាញយក FFmpeg",
        [nameof(NothingFoundTitle)] = "រកមិនឃើញអ្វីទេ",
        [nameof(NothingFoundMessage)] =
            "រកមិនឃើញវីដេអូណាមួយផ្អែកលើពាក្យស្វែងរក ឬ URL ដែលអ្នកបានផ្តល់",
        [nameof(ErrorTitle)] = "កំហុស",
        [nameof(UpdateDownloadingMessage)] = "កំពុងទាញយកបច្ចុប្បន្នភាពទៅ {0} v{1}...",
        [nameof(UpdateReadyMessage)] = "បានទាញយកបច្ចុប្បន្នភាព ហើយវានឹងត្រូវបានដំឡើងពេលអ្នកចាកចេញ",
        [nameof(UpdateInstallNowButton)] = "ដំឡើងឥឡូវនេះ",
        [nameof(UpdateFailedMessage)] = "បរាជ័យក្នុងការធ្វើបច្ចុប្បន្នភាពកម្មវិធី",
    };
}
