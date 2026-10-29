namespace AIExtensions.Sample.ChatPlayground;

public sealed record EmbeddingSampleDocument(string Id, string Name, string Text);

/// <summary>Synthetic local documents. Loading these never contacts an embedding provider.</summary>
public static class EmbeddingSampleDocuments
{
    public static IReadOnlyList<EmbeddingSampleDocument> All { get; } = Array.AsReadOnly<EmbeddingSampleDocument>(
    [
        new("harbor-routes", "Harbor routes and transport", """
            # Harbor routes and transport

            The Willow Loop is a flat walking trail around the freshwater lagoon. Its wooden boardwalk has benches every half kilometer and is suitable for wheelchairs, but bicycles are not allowed on the boardwalk.

            For a steep hike, take the basalt stairway from Quarry Gate to the weather station on North Ridge. The summit has no drinking water, so hikers should fill their bottles at Quarry Gate before starting the climb.

            Bus 24 connects the railway station to Quarry Gate every twenty minutes on weekdays. On Sundays it runs hourly, and the last return bus leaves the gate at 18:10.

            The passenger ferry crosses from Harbor Pier to Lantern Island in twelve minutes. Bicycles travel free on the rear deck, but the ferry does not carry cars and stops operating during strong winds.

            Kayaks can be hired at Reed Cove for the sheltered inlet route. Paddlers must wear the supplied buoyancy vest and keep away from the marked heron nesting area on the eastern bank.

            At the old signal tower, volunteer guides run sunset birdwatching walks on Saturdays. Bring binoculars and a warm jacket; flash photography is prohibited near the nesting birds.
            """),
        new("developer-runbook", "Developer troubleshooting and security", """
            # Developer troubleshooting and security

            When a local web server refuses a connection, first check that its process is running and listening on the configured port. If another process owns port 8080, stop only the identified process or choose a different port in the launch settings.

            A successful HTTP response with stale content usually points to a cache rather than a network failure. Clear the browser cache and compare the response version header before rebuilding the application.

            Database migrations must be tested against a restored backup before production deployment. Take a fresh backup, run the migration in staging, and verify that rollback restores the original schema and data.

            Store API keys in the operating system credential vault or the deployment secret store, never in source control. If a credential appears in a log or commit, revoke it immediately and issue a replacement; deleting the visible text is not sufficient.

            Access tokens expire after fifteen minutes in this demonstration service. Refresh them through the authentication endpoint, and never put bearer tokens in URL query strings because proxies can record those URLs.

            To investigate a slow background job, capture its trace identifier and inspect the database spans. Increasing worker concurrency can make lock contention worse, so measure queue latency and transaction duration before raising the worker count.
            """),
        new("copper-kettle", "Copper Kettle cafe bookings and food", """
            # Copper Kettle cafe bookings and food

            The Copper Kettle cafe serves breakfast from 07:30 until 11:00 on weekdays. Its breakfast menu includes mushroom toast, oat porridge with berries, and soft scrambled eggs on sourdough.

            Table bookings for up to six people can be made online until two hours before arrival. Larger groups must telephone the manager, and a window table is a request rather than a guaranteed part of a reservation.

            The courtyard is dog-friendly and has shaded outdoor seating beside the herb garden. Dogs must remain on a leash, and only assistance animals are permitted in the indoor dining room.

            The lentil soup and roasted vegetable bowl are vegan, while the almond cake contains both nuts and dairy. Tell staff about allergies before ordering because the small kitchen uses shared preparation surfaces.

            Coffee beans from the mountain cooperative are roasted on Thursdays and sold in recyclable bags. The espresso blend has chocolate notes, while the seasonal filter coffee is lighter with citrus aromas.

            For private evening events, the upstairs room seats eighteen guests and has a projector with an HDMI connection. A deposit confirms the booking, and cancellations at least seventy-two hours before the event receive a full deposit refund.
            """),
    ]);
}
