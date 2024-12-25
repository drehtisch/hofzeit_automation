import asyncio
import requests
from TikTokLive import TikTokLiveClient
from TikTokLive.client.logger import LogLevel
from TikTokLive.events import ConnectEvent, DisconnectEvent, LiveEndEvent, ControlEvent
from TikTokLive.proto import ControlAction

# Zentrale Konfiguration
STREAMER_BOT_URL = "http://localhost:7474"
API_KEY = ""
LIVE_ACTION = "LiveStart"
OFFLINE_ACTION = "LiveEnd"

# Client initialisieren
client: TikTokLiveClient = TikTokLiveClient(
    unique_id="@hofzeitprojekt"
    # unique_id="@tv_asahi_news"  # Test-Kanal, der immer live ist
)

@client.on(ConnectEvent)
async def on_connect(event: ConnectEvent):
    """
    Event-Handler für eine erfolgreiche Verbindung.
    """
    client.logger.info(f"Connected to @{event.unique_id}!")
    trigger_streamer_bot(LIVE_ACTION)


@client.on(DisconnectEvent)
async def on_disconnect(event: DisconnectEvent):
    """
    Event-Handler bei Verbindungsabbruch.
    """
    client.logger.info("Disconnected.")


@client.on(LiveEndEvent)
async def on_liveend(event: LiveEndEvent):
    """
    Event-Handler für das Beenden eines Live-Streams.
    """
    client.logger.info("Live-Stream beendet.")
    trigger_streamer_bot(OFFLINE_ACTION)


@client.on(ControlEvent)
async def on_control(event: ControlEvent):
    """
    Event-Handler für Steuerungsereignisse.
    """
    client.logger.info(f"Control event {ControlAction(event.action).name}")


def trigger_streamer_bot(action_name):
    """
    Sendet eine POST-Anfrage an Streamer.bot, um eine Aktion auszulösen.

    Args:
        action_name (str): Der Name der Aktion in Streamer.bot.
    """
    try:
        endpoint = f"{STREAMER_BOT_URL}/DoAction"
        headers = {"Content-Type": "application/json"}

        if API_KEY:
            headers["Authorization"] = f"Bearer {API_KEY}"

        data = {"action": {"name": action_name}}
        response = requests.post(endpoint, json=data, headers=headers)
        response.raise_for_status()

        client.logger.info(f"Aktion '{action_name}' erfolgreich an Streamer.bot gesendet.")
    except requests.RequestException as e:
        client.logger.error(f"Fehler beim Senden der Aktion an Streamer.bot: {e}")


async def check_loop():
    """
    Haupt-Loop zur Überprüfung des Live-Status und Verbindungsaufbau.
    """
    try:
        while True:
            try:
                is_live = await client.is_live()

                if is_live:
                    client.logger.info("Kanal ist live. Verbindungsversuch...")
                    if not client.connected:
                        await client.connect()

                else:
                    client.logger.info("Kanal ist offline. Warte auf Live-Start...")

                # Wartezeit zwischen Überprüfungen
                await asyncio.sleep(60)

            except Exception as e:
                client.logger.error(f"Fehler im Check-Loop: {e}")
                await asyncio.sleep(60)  # Wartezeit nach Fehler
    except asyncio.CancelledError:
        client.logger.info("Check-Loop wurde durch Abbruchsignal beendet.")


async def main():
    """
    Hauptfunktion, die das Programm ausführt.
    """
    print("Gerys und Tylix TikTok Live Checker")
    client.logger.setLevel(LogLevel.INFO.value)

    # Asyncio-Task für den Haupt-Loop erstellen
    check_task = asyncio.create_task(check_loop())

    try:
        await check_task
    except asyncio.CancelledError:
        client.logger.info("Beenden erkannt. Schließe das Programm sauber.")
        check_task.cancel()
        await check_task


if __name__ == '__main__':
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("Programm wurde durch STRG+C beendet.")
