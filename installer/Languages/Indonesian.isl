; *** Inno Setup version 6.5.0+ Indonesian messages ***
;
; Translated for the Astra Finance installer by Aprotoos (2026), based on Inno Setup's Default.isl.
; Distributed with the Astra Finance installer sources under the same terms as Inno Setup's Default.isl.
;
; Note: as in Default.isl, messages without a final period don't get one here, because Inno Setup adds
; the period itself where needed.

[LangOptions]
LanguageName=Bahasa Indonesia
LanguageID=$0421
LanguageCodePage=1252

[Messages]

; *** Application titles
SetupAppTitle=Instalasi
SetupWindowTitle=Instalasi - %1
UninstallAppTitle=Hapus Instalasi
UninstallAppFullTitle=Hapus Instalasi %1

; *** Misc. common
InformationTitle=Informasi
ConfirmTitle=Konfirmasi
ErrorTitle=Kesalahan

; *** SetupLdr messages
SetupLdrStartupMessage=Program ini akan memasang %1. Lanjutkan?
LdrCannotCreateTemp=Tidak dapat membuat berkas sementara. Instalasi dibatalkan
LdrCannotExecTemp=Tidak dapat menjalankan berkas di folder sementara. Instalasi dibatalkan
HelpTextNote=

; *** Startup error messages
LastErrorMessage=%1.%n%nKesalahan %2: %3
SetupFileMissing=Berkas %1 tidak ditemukan di folder instalasi. Perbaiki masalah ini atau dapatkan salinan program yang baru.
SetupFileCorrupt=Berkas instalasi rusak. Dapatkan salinan program yang baru.
SetupFileCorruptOrWrongVer=Berkas instalasi rusak atau tidak cocok dengan versi Instalasi ini. Perbaiki masalah ini atau dapatkan salinan program yang baru.
InvalidParameter=Parameter yang tidak valid diberikan pada baris perintah:%n%n%1
SetupAlreadyRunning=Instalasi sudah berjalan.
WindowsVersionNotSupported=Program ini tidak mendukung versi Windows yang digunakan komputer Anda.
WindowsServicePackRequired=Program ini memerlukan %1 Service Pack %2 atau yang lebih baru.
NotOnThisPlatform=Program ini tidak dapat berjalan di %1.
OnlyOnThisPlatform=Program ini harus dijalankan di %1.
OnlyOnTheseArchitectures=Program ini hanya dapat dipasang pada versi Windows untuk arsitektur prosesor berikut:%n%n%1
WinVersionTooLowError=Program ini memerlukan %1 versi %2 atau yang lebih baru.
WinVersionTooHighError=Program ini tidak dapat dipasang pada %1 versi %2 atau yang lebih baru.
AdminPrivilegesRequired=Anda harus masuk sebagai administrator untuk memasang program ini.
PowerUserPrivilegesRequired=Anda harus masuk sebagai administrator atau anggota grup Power Users untuk memasang program ini.
SetupAppRunningError=Instalasi mendeteksi bahwa %1 sedang berjalan.%n%nTutup semua jendelanya sekarang, lalu klik OK untuk melanjutkan, atau Batal untuk keluar.
UninstallAppRunningError=Penghapusan instalasi mendeteksi bahwa %1 sedang berjalan.%n%nTutup semua jendelanya sekarang, lalu klik OK untuk melanjutkan, atau Batal untuk keluar.

; *** Startup questions
PrivilegesRequiredOverrideTitle=Pilih Mode Instalasi
PrivilegesRequiredOverrideInstruction=Pilih mode instalasi
PrivilegesRequiredOverrideText1=%1 dapat dipasang untuk semua pengguna (memerlukan hak administrator), atau hanya untuk Anda.
PrivilegesRequiredOverrideText2=%1 dapat dipasang hanya untuk Anda, atau untuk semua pengguna (memerlukan hak administrator).
PrivilegesRequiredOverrideAllUsers=Pasang untuk &semua pengguna
PrivilegesRequiredOverrideAllUsersRecommended=Pasang untuk &semua pengguna (disarankan)
PrivilegesRequiredOverrideCurrentUser=Pasang hanya untuk sa&ya
PrivilegesRequiredOverrideCurrentUserRecommended=Pasang hanya untuk sa&ya (disarankan)

; *** Misc. errors
ErrorCreatingDir=Instalasi tidak dapat membuat folder "%1"
ErrorTooManyFilesInDir=Tidak dapat membuat berkas di folder "%1" karena folder itu berisi terlalu banyak berkas

; *** Setup common messages
ExitSetupTitle=Keluar dari Instalasi
ExitSetupMessage=Instalasi belum selesai. Jika Anda keluar sekarang, program tidak akan terpasang.%n%nAnda dapat menjalankan Instalasi lagi lain kali untuk menyelesaikannya.%n%nKeluar dari Instalasi?
AboutSetupMenuItem=&Tentang Instalasi...
AboutSetupTitle=Tentang Instalasi
AboutSetupMessage=%1 versi %2%n%3%n%nSitus %1:%n%4
AboutSetupNote=
TranslatorNote=

; *** Buttons
ButtonBack=< &Kembali
ButtonNext=&Lanjut >
ButtonInstall=&Pasang
ButtonOK=OK
ButtonCancel=Batal
ButtonYes=&Ya
ButtonYesToAll=Ya untuk &Semua
ButtonNo=&Tidak
ButtonNoToAll=T&idak untuk Semua
ButtonFinish=&Selesai
ButtonBrowse=&Telusuri...
ButtonWizardBrowse=T&elusuri...
ButtonNewFolder=&Buat Folder Baru

; *** "Select Language" dialog messages
SelectLanguageTitle=Pilih Bahasa Instalasi
SelectLanguageLabel=Pilih bahasa yang digunakan selama instalasi.

; *** Common wizard text
ClickNext=Klik Lanjut untuk meneruskan, atau Batal untuk keluar dari Instalasi.
BeveledLabel=
BrowseDialogTitle=Telusuri Folder
BrowseDialogLabel=Pilih folder dari daftar di bawah, lalu klik OK.
NewFolderName=Folder Baru

; *** "Welcome" wizard page
WelcomeLabel1=Selamat datang di Wizard Instalasi [name]
WelcomeLabel2=Program ini akan memasang [name/ver] di komputer Anda.%n%nSebaiknya tutup semua aplikasi lain sebelum melanjutkan.

; *** "Password" wizard page
WizardPassword=Kata Sandi
PasswordLabel1=Instalasi ini dilindungi kata sandi.
PasswordLabel3=Masukkan kata sandi, lalu klik Lanjut. Huruf besar dan kecil dibedakan.
PasswordEditLabel=&Kata sandi:
IncorrectPassword=Kata sandi yang Anda masukkan salah. Silakan coba lagi.

; *** "License Agreement" wizard page
WizardLicense=Perjanjian Lisensi
LicenseLabel=Bacalah informasi penting berikut sebelum melanjutkan.
LicenseLabel3=Bacalah Perjanjian Lisensi berikut. Anda harus menyetujui ketentuan perjanjian ini sebelum melanjutkan instalasi.
LicenseAccepted=Saya &menyetujui perjanjian ini
LicenseNotAccepted=Saya &tidak menyetujui perjanjian ini

; *** "Information" wizard pages
WizardInfoBefore=Informasi
InfoBeforeLabel=Bacalah informasi penting berikut sebelum melanjutkan.
InfoBeforeClickLabel=Jika sudah siap melanjutkan Instalasi, klik Lanjut.
WizardInfoAfter=Informasi
InfoAfterLabel=Bacalah informasi penting berikut sebelum melanjutkan.
InfoAfterClickLabel=Jika sudah siap melanjutkan Instalasi, klik Lanjut.

; *** "User Information" wizard page
WizardUserInfo=Informasi Pengguna
UserInfoDesc=Masukkan informasi Anda.
UserInfoName=&Nama pengguna:
UserInfoOrg=&Organisasi:
UserInfoSerial=Nomor &seri:
UserInfoNameRequired=Anda harus memasukkan nama.

; *** "Select Destination Location" wizard page
WizardSelectDir=Pilih Lokasi Tujuan
SelectDirDesc=Di mana [name] akan dipasang?
SelectDirLabel3=Instalasi akan memasang [name] ke folder berikut.
SelectDirBrowseLabel=Untuk melanjutkan, klik Lanjut. Jika ingin memilih folder lain, klik Telusuri.
DiskSpaceGBLabel=Diperlukan ruang kosong minimal [gb] GB.
DiskSpaceMBLabel=Diperlukan ruang kosong minimal [mb] MB.
CannotInstallToNetworkDrive=Instalasi tidak dapat memasang ke drive jaringan.
CannotInstallToUNCPath=Instalasi tidak dapat memasang ke jalur UNC.
InvalidPath=Anda harus memasukkan jalur lengkap dengan huruf drive, misalnya:%n%nC:\APP%n%natau jalur UNC dengan format:%n%n\\server\share
InvalidDrive=Drive atau folder bersama UNC yang Anda pilih tidak ada atau tidak dapat diakses. Silakan pilih yang lain.
DiskSpaceWarningTitle=Ruang Disk Tidak Cukup
DiskSpaceWarning=Instalasi memerlukan ruang kosong minimal %1 KB, tetapi drive yang dipilih hanya memiliki %2 KB.%n%nTetap lanjutkan?
DirNameTooLong=Nama folder atau jalurnya terlalu panjang.
InvalidDirName=Nama folder tidak valid.
BadDirName32=Nama folder tidak boleh mengandung karakter berikut:%n%n%1
DirExistsTitle=Folder Sudah Ada
DirExists=Folder:%n%n%1%n%nsudah ada. Tetap pasang ke folder tersebut?
DirDoesntExistTitle=Folder Tidak Ada
DirDoesntExist=Folder:%n%n%1%n%ntidak ada. Buat folder tersebut?

; *** "Select Components" wizard page
WizardSelectComponents=Pilih Komponen
SelectComponentsDesc=Komponen mana yang akan dipasang?
SelectComponentsLabel2=Pilih komponen yang ingin dipasang dan hapus centang pada komponen yang tidak ingin dipasang. Klik Lanjut jika sudah siap.
FullInstallation=Instalasi lengkap
CompactInstallation=Instalasi ringkas
CustomInstallation=Instalasi khusus
NoUninstallWarningTitle=Komponen Sudah Ada
NoUninstallWarning=Instalasi mendeteksi bahwa komponen berikut sudah terpasang di komputer Anda:%n%n%1%n%nMembatalkan pilihan komponen ini tidak akan menghapusnya.%n%nTetap lanjutkan?
ComponentSize1=%1 KB
ComponentSize2=%1 MB
ComponentsDiskSpaceGBLabel=Pilihan saat ini memerlukan ruang disk minimal [gb] GB.
ComponentsDiskSpaceMBLabel=Pilihan saat ini memerlukan ruang disk minimal [mb] MB.

; *** "Select Additional Tasks" wizard page
WizardSelectTasks=Pilih Tugas Tambahan
SelectTasksDesc=Tugas tambahan apa yang perlu dilakukan?
SelectTasksLabel2=Pilih tugas tambahan yang ingin dilakukan Instalasi saat memasang [name], lalu klik Lanjut.

; *** "Select Start Menu Folder" wizard page
WizardSelectProgramGroup=Pilih Folder Menu Start
SelectStartMenuFolderDesc=Di mana Instalasi harus menaruh pintasan program?
SelectStartMenuFolderLabel3=Instalasi akan membuat pintasan program di folder Menu Start berikut.
SelectStartMenuFolderBrowseLabel=Untuk melanjutkan, klik Lanjut. Jika ingin memilih folder lain, klik Telusuri.
MustEnterGroupName=Anda harus memasukkan nama folder.
GroupNameTooLong=Nama folder atau jalurnya terlalu panjang.
InvalidGroupName=Nama folder tidak valid.
BadGroupName=Nama folder tidak boleh mengandung karakter berikut:%n%n%1
NoProgramGroupCheck2=&Jangan buat folder Menu Start

; *** "Ready to Install" wizard page
WizardReady=Siap Memasang
ReadyLabel1=Instalasi siap mulai memasang [name] di komputer Anda.
ReadyLabel2a=Klik Pasang untuk melanjutkan instalasi, atau klik Kembali jika ingin meninjau atau mengubah pengaturan.
ReadyLabel2b=Klik Pasang untuk melanjutkan instalasi.
ReadyMemoUserInfo=Informasi pengguna:
ReadyMemoDir=Lokasi tujuan:
ReadyMemoType=Jenis instalasi:
ReadyMemoComponents=Komponen yang dipilih:
ReadyMemoGroup=Folder Menu Start:
ReadyMemoTasks=Tugas tambahan:

; *** TDownloadWizardPage wizard page and DownloadTemporaryFile
DownloadingLabel2=Mengunduh berkas...
ButtonStopDownload=&Hentikan unduhan
StopDownload=Yakin ingin menghentikan unduhan?
ErrorDownloadAborted=Unduhan dibatalkan
ErrorDownloadFailed=Unduhan gagal: %1 %2
ErrorDownloadSizeFailed=Gagal mendapatkan ukuran: %1 %2
ErrorProgress=Kemajuan tidak valid: %1 dari %2
ErrorFileSize=Ukuran berkas tidak valid: seharusnya %1, ditemukan %2

; *** TExtractionWizardPage wizard page and ExtractArchive
ExtractingLabel=Mengekstrak berkas...
ButtonStopExtraction=&Hentikan ekstraksi
StopExtraction=Yakin ingin menghentikan ekstraksi?
ErrorExtractionAborted=Ekstraksi dibatalkan
ErrorExtractionFailed=Ekstraksi gagal: %1

; *** Archive extraction failure details
ArchiveIncorrectPassword=Kata sandi salah
ArchiveIsCorrupted=Arsip rusak
ArchiveUnsupportedFormat=Format arsip tidak didukung

; *** "Preparing to Install" wizard page
WizardPreparing=Mempersiapkan Instalasi
PreparingDesc=Instalasi sedang mempersiapkan pemasangan [name] di komputer Anda.
PreviousInstallNotCompleted=Instalasi atau penghapusan program sebelumnya belum selesai. Anda perlu memulai ulang komputer untuk menyelesaikannya.%n%nSetelah memulai ulang komputer, jalankan Instalasi lagi untuk menyelesaikan pemasangan [name].
CannotContinue=Instalasi tidak dapat dilanjutkan. Klik Batal untuk keluar.
ApplicationsFound=Aplikasi berikut menggunakan berkas yang perlu diperbarui oleh Instalasi. Sebaiknya izinkan Instalasi menutup aplikasi ini secara otomatis.
ApplicationsFound2=Aplikasi berikut menggunakan berkas yang perlu diperbarui oleh Instalasi. Sebaiknya izinkan Instalasi menutup aplikasi ini secara otomatis. Setelah instalasi selesai, Instalasi akan mencoba menjalankan kembali aplikasi tersebut.
CloseApplications=&Tutup aplikasi secara otomatis
DontCloseApplications=&Jangan tutup aplikasi
ErrorCloseApplications=Instalasi tidak dapat menutup semua aplikasi secara otomatis. Sebaiknya tutup semua aplikasi yang menggunakan berkas yang perlu diperbarui sebelum melanjutkan.
PrepareToInstallNeedsRestart=Instalasi harus memulai ulang komputer Anda. Setelah itu, jalankan Instalasi lagi untuk menyelesaikan pemasangan [name].%n%nMulai ulang sekarang?

; *** "Installing" wizard page
WizardInstalling=Memasang
InstallingLabel=Mohon tunggu, Instalasi sedang memasang [name] di komputer Anda.

; *** "Setup Completed" wizard page
FinishedHeadingLabel=Menyelesaikan Wizard Instalasi [name]
FinishedLabelNoIcons=Instalasi telah selesai memasang [name] di komputer Anda.
FinishedLabel=Instalasi telah selesai memasang [name] di komputer Anda. Aplikasi dapat dibuka melalui pintasan yang telah dipasang.
ClickFinish=Klik Selesai untuk keluar dari Instalasi.
FinishedRestartLabel=Untuk menyelesaikan pemasangan [name], Instalasi harus memulai ulang komputer Anda. Mulai ulang sekarang?
FinishedRestartMessage=Untuk menyelesaikan pemasangan [name], Instalasi harus memulai ulang komputer Anda.%n%nMulai ulang sekarang?
ShowReadmeCheck=Ya, saya ingin membaca berkas README
YesRadio=&Ya, mulai ulang komputer sekarang
NoRadio=&Tidak, saya akan memulai ulang komputer nanti
RunEntryExec=Jalankan %1
RunEntryShellExec=Lihat %1

; *** "Setup Needs the Next Disk" stuff
ChangeDiskTitle=Instalasi Memerlukan Disk Berikutnya
SelectDiskLabel2=Masukkan Disk %1 dan klik OK.%n%nJika berkas di disk ini ada di folder selain yang ditampilkan di bawah, masukkan jalur yang benar atau klik Telusuri.
PathLabel=&Jalur:
FileNotInDir2=Berkas "%1" tidak ditemukan di "%2". Masukkan disk yang benar atau pilih folder lain.
SelectDirectoryLabel=Tentukan lokasi disk berikutnya.

; *** Installation phase messages
SetupAborted=Instalasi tidak selesai.%n%nPerbaiki masalahnya, lalu jalankan Instalasi lagi.
AbortRetryIgnoreSelectAction=Pilih tindakan
AbortRetryIgnoreRetry=&Coba lagi
AbortRetryIgnoreIgnore=&Abaikan kesalahan dan lanjutkan
AbortRetryIgnoreCancel=Batalkan instalasi
RetryCancelSelectAction=Pilih tindakan
RetryCancelRetry=&Coba lagi
RetryCancelCancel=Batal

; *** Installation status messages
StatusClosingApplications=Menutup aplikasi...
StatusCreateDirs=Membuat folder...
StatusExtractFiles=Mengekstrak berkas...
StatusDownloadFiles=Mengunduh berkas...
StatusCreateIcons=Membuat pintasan...
StatusCreateIniEntries=Membuat entri INI...
StatusCreateRegistryEntries=Membuat entri registri...
StatusRegisterFiles=Mendaftarkan berkas...
StatusSavingUninstall=Menyimpan informasi penghapusan instalasi...
StatusRunProgram=Menyelesaikan instalasi...
StatusRestartingApplications=Menjalankan kembali aplikasi...
StatusRollback=Membatalkan perubahan...

; *** Misc. errors
ErrorInternal2=Kesalahan internal: %1
ErrorFunctionFailedNoCode=%1 gagal
ErrorFunctionFailed=%1 gagal; kode %2
ErrorFunctionFailedWithMessage=%1 gagal; kode %2.%n%3
ErrorExecutingProgram=Tidak dapat menjalankan berkas:%n%1

; *** Registry errors
ErrorRegOpenKey=Kesalahan saat membuka kunci registri:%n%1\%2
ErrorRegCreateKey=Kesalahan saat membuat kunci registri:%n%1\%2
ErrorRegWriteKey=Kesalahan saat menulis ke kunci registri:%n%1\%2

; *** INI errors
ErrorIniEntry=Kesalahan saat membuat entri INI di berkas "%1".

; *** File copying errors
FileAbortRetryIgnoreSkipNotRecommended=&Lewati berkas ini (tidak disarankan)
FileAbortRetryIgnoreIgnoreNotRecommended=&Abaikan kesalahan dan lanjutkan (tidak disarankan)
SourceIsCorrupted=Berkas sumber rusak
SourceDoesntExist=Berkas sumber "%1" tidak ada
SourceVerificationFailed=Verifikasi berkas sumber gagal: %1
VerificationSignatureDoesntExist=Berkas tanda tangan "%1" tidak ada
VerificationSignatureInvalid=Berkas tanda tangan "%1" tidak valid
VerificationKeyNotFound=Berkas tanda tangan "%1" menggunakan kunci yang tidak dikenal
VerificationFileNameIncorrect=Nama berkas salah
VerificationFileTagIncorrect=Tag berkas salah
VerificationFileSizeIncorrect=Ukuran berkas salah
VerificationFileHashIncorrect=Hash berkas salah
ExistingFileReadOnly2=Berkas yang ada tidak dapat diganti karena bersifat hanya-baca.
ExistingFileReadOnlyRetry=&Hapus atribut hanya-baca dan coba lagi
ExistingFileReadOnlyKeepExisting=&Pertahankan berkas yang ada
ErrorReadingExistingDest=Terjadi kesalahan saat membaca berkas yang ada:
FileExistsSelectAction=Pilih tindakan
FileExists2=Berkas sudah ada.
FileExistsOverwriteExisting=&Timpa berkas yang ada
FileExistsKeepExisting=&Pertahankan berkas yang ada
FileExistsOverwriteOrKeepAll=&Lakukan ini untuk konflik berikutnya
ExistingFileNewerSelectAction=Pilih tindakan
ExistingFileNewer2=Berkas yang ada lebih baru daripada berkas yang akan dipasang Instalasi.
ExistingFileNewerOverwriteExisting=&Timpa berkas yang ada
ExistingFileNewerKeepExisting=&Pertahankan berkas yang ada (disarankan)
ExistingFileNewerOverwriteOrKeepAll=&Lakukan ini untuk konflik berikutnya
ErrorChangingAttr=Terjadi kesalahan saat mengubah atribut berkas yang ada:
ErrorCreatingTemp=Terjadi kesalahan saat membuat berkas di folder tujuan:
ErrorReadingSource=Terjadi kesalahan saat membaca berkas sumber:
ErrorCopying=Terjadi kesalahan saat menyalin berkas:
ErrorDownloading=Terjadi kesalahan saat mengunduh berkas:
ErrorExtracting=Terjadi kesalahan saat mengekstrak arsip:
ErrorReplacingExistingFile=Terjadi kesalahan saat mengganti berkas yang ada:
ErrorRestartReplace=RestartReplace gagal:
ErrorRenamingTemp=Terjadi kesalahan saat mengganti nama berkas di folder tujuan:
ErrorRegisterServer=Tidak dapat mendaftarkan DLL/OCX: %1
ErrorRegSvr32Failed=RegSvr32 gagal dengan kode keluar %1
ErrorRegisterTypeLib=Tidak dapat mendaftarkan pustaka tipe: %1

; *** Uninstall display name markings
UninstallDisplayNameMark=%1 (%2)
UninstallDisplayNameMarks=%1 (%2, %3)
UninstallDisplayNameMark32Bit=32-bit
UninstallDisplayNameMark64Bit=64-bit
UninstallDisplayNameMarkAllUsers=Semua pengguna
UninstallDisplayNameMarkCurrentUser=Pengguna saat ini

; *** Post-installation errors
ErrorOpeningReadme=Terjadi kesalahan saat membuka berkas README.
ErrorRestartingComputer=Instalasi tidak dapat memulai ulang komputer. Silakan lakukan secara manual.

; *** Uninstaller messages
UninstallNotFound=Berkas "%1" tidak ada. Tidak dapat menghapus instalasi.
UninstallOpenError=Berkas "%1" tidak dapat dibuka. Tidak dapat menghapus instalasi
UninstallUnsupportedVer=Format berkas log penghapusan "%1" tidak dikenali oleh versi penghapus instalasi ini. Tidak dapat menghapus instalasi
UninstallUnknownEntry=Ditemukan entri yang tidak dikenal (%1) di log penghapusan instalasi
ConfirmUninstall=Yakin ingin menghapus %1 beserta semua komponennya dari komputer ini?
UninstallOnlyOnWin64=Instalasi ini hanya dapat dihapus di Windows 64-bit.
OnlyAdminCanUninstall=Instalasi ini hanya dapat dihapus oleh pengguna dengan hak administrator.
UninstallStatusLabel=Mohon tunggu, %1 sedang dihapus dari komputer Anda.
UninstalledAll=%1 berhasil dihapus dari komputer Anda.
UninstalledMost=Penghapusan %1 selesai.%n%nBeberapa elemen tidak dapat dihapus dan dapat dihapus secara manual.
UninstalledAndNeedsRestart=Untuk menyelesaikan penghapusan %1, komputer Anda harus dimulai ulang.%n%nMulai ulang sekarang?
UninstallDataCorrupted=Berkas "%1" rusak. Tidak dapat menghapus instalasi

; *** Uninstallation phase messages
ConfirmDeleteSharedFileTitle=Hapus Berkas Bersama?
ConfirmDeleteSharedFile2=Sistem menunjukkan bahwa berkas bersama berikut tidak lagi digunakan oleh program mana pun. Hapus berkas bersama ini?%n%nJika masih ada program yang menggunakannya dan berkas ini dihapus, program tersebut mungkin tidak berfungsi dengan baik. Jika ragu, pilih Tidak. Membiarkan berkas ini tidak akan menimbulkan masalah.
SharedFileNameLabel=Nama berkas:
SharedFileLocationLabel=Lokasi:
WizardUninstalling=Status Penghapusan Instalasi
StatusUninstalling=Menghapus %1...

; *** Shutdown block reasons
ShutdownBlockReasonInstallingApp=Memasang %1.
ShutdownBlockReasonUninstallingApp=Menghapus %1.

[CustomMessages]

NameAndVersion=%1 versi %2
AdditionalIcons=Pintasan tambahan:
CreateDesktopIcon=Buat pintasan di &desktop
CreateQuickLaunchIcon=Buat pintasan &Quick Launch
ProgramOnTheWeb=%1 di Web
UninstallProgram=Hapus instalasi %1
LaunchProgram=Jalankan %1
AssocFileExtension=&Kaitkan %1 dengan ekstensi berkas %2
AssocingFileExtension=Mengaitkan %1 dengan ekstensi berkas %2...
AutoStartProgramGroupDescription=Startup:
AutoStartProgram=Jalankan %1 secara otomatis
AddonHostProgramNotFound=%1 tidak ditemukan di folder yang Anda pilih.%n%nTetap lanjutkan?
