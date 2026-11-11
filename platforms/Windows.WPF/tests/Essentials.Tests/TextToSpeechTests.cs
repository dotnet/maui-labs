using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;

namespace Essentials.Tests;

public sealed class TextToSpeechTests
{
	[Fact]
	public void Locale_ContainsUsableVoiceMetadata()
	{
		var locale = WPFTextToSpeech.CreateLocale(
			"en",
			"US",
			"Microsoft David",
			"HKEY_LOCAL_MACHINE/Voices/David");

		Assert.Equal("en", locale.Language);
		Assert.Equal("US", locale.Country);
		Assert.Equal("Microsoft David", locale.Name);
		Assert.Equal("HKEY_LOCAL_MACHINE/Voices/David", locale.Id);
	}

	[Fact]
	public void VoiceSelection_PrefersStableVoiceId()
	{
		var locale = WPFTextToSpeech.CreateLocale(
			"en",
			"US",
			"Shared display name",
			"voice-two");

		Assert.False(WPFTextToSpeech.MatchesVoice(
			"voice-one",
			"Shared display name",
			"en-US",
			"US",
			locale));
		Assert.True(WPFTextToSpeech.MatchesVoice(
			"voice-two",
			"Different display name",
			"fr-FR",
			"FR",
			locale));
	}

	[Theory]
	[InlineData("en", "", "en-US", "US", true)]
	[InlineData("en", "GB", "en-GB", "GB", true)]
	[InlineData("en", "GB", "en-US", "US", false)]
	[InlineData("en-US", "", "en-US", "US", true)]
	[InlineData("fr", "", "en-US", "US", false)]
	public void VoiceSelection_UsesLanguageAndCountryWhenIdIsEmpty(
		string language,
		string country,
		string voiceLanguage,
		string voiceCountry,
		bool expected)
	{
		var locale = WPFTextToSpeech.CreateLocale(language, country, "", "");

		Assert.Equal(expected, WPFTextToSpeech.MatchesVoice(
			"voice-id",
			"Voice name",
			voiceLanguage,
			voiceCountry,
			locale));
	}

	[Fact]
	public void VoiceSelection_UsesNameOnlyWhenIdAndLanguageAreEmpty()
	{
		var locale = WPFTextToSpeech.CreateLocale("", "", "Voice name", "");

		Assert.True(WPFTextToSpeech.MatchesVoice(
			"voice-id",
			"Voice name",
			"en-US",
			"US",
			locale));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void SpeechValidation_RejectsNullOrEmptyText(string? text)
	{
		Assert.Throws<ArgumentNullException>(() =>
			WPFTextToSpeech.ValidateSpeechRequest(text!, null));
	}

	[Fact]
	public void SpeechValidation_RejectsInvalidOptionsAndUnsupportedPitch()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			WPFTextToSpeech.ValidateSpeechRequest("text", new SpeechOptions { Volume = 1.01f }));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			WPFTextToSpeech.ValidateSpeechRequest("text", new SpeechOptions { Rate = 0.09f }));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			WPFTextToSpeech.ValidateSpeechRequest("text", new SpeechOptions { Pitch = 2.01f }));
		Assert.Throws<FeatureNotSupportedException>(() =>
			WPFTextToSpeech.ValidateSpeechRequest("text", new SpeechOptions { Pitch = 1f }));
	}

	[Fact]
	public void SpeechValidation_AcceptsContractBoundaries()
	{
		WPFTextToSpeech.ValidateSpeechRequest(
			" ",
			new SpeechOptions { Volume = 0f, Rate = 0.1f });
		WPFTextToSpeech.ValidateSpeechRequest(
			"text",
			new SpeechOptions { Volume = 1f, Rate = 2f });
	}
}
