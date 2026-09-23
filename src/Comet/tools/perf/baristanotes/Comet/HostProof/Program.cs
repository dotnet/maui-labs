if (args.Length == 4 && args[3] is
    "check-malformed-ranges" or "check-unsupported-ranges" or "check-aliased-namespace")
    await Comet.Tests.BaristaFixtures.CometFixtureCorrectionProof.RunAsync(args);
else
    await Comet.Tests.BaristaFixtures.CometFixtureHostProof.RunAsync(args);
