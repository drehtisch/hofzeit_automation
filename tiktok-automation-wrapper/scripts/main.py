import asyncio
import requests
from TikTokLive import TikTokLiveClient
from TikTokLive.client.logger import LogLevel
from TikTokLive.events import ConnectEvent, DisconnectEvent, LiveEndEvent, ControlEvent
from TikTokLive.proto import ControlAction

# Client initialisieren
client: TikTokLiveClient = TikTokLiveClient(
    unique_id="@hofzeitprojekt"
)
live_action = "LiveStart"
offline_action = "LiveEnd"
streamer_bot_url = "http://localhost:7474"
api_key = ""

def trigger_streamer_bot(action_name, server_url, api_key=""):
    """
    Sendet eine POST-Anfrage an Streamer.bot, um eine Aktion auszulösen.

    Args:
        action_name (str): Der Name der Aktion in Streamer.bot.
        server_url (str): Die URL des Streamer.bot-Servers.
        api_key (str): Optionaler API-Schlüssel, falls konfiguriert.
    """
    try:
        endpoint = f"{server_url}/DoAction"
        headers = {"Content-Type": "application/json"}

        if api_key:
            headers["Authorization"] = f"Bearer {api_key}"

        data = {"action": {"name": action_name}}
        response = requests.post(endpoint, json=data, headers=headers)
        response.raise_for_status()

        client.logger.info(f"Aktion '{action_name}' erfolgreich an Streamer.bot gesendet.")
    except requests.RequestException as e:
        client.logger.error(f"Fehler beim Senden der Aktion an Streamer.bot: {e}")



@client.on(ConnectEvent)
async def on_connect(event: ConnectEvent):
    """
    Event-Handler für eine erfolgreiche Verbindung.
    """
    client.logger.info(f"Connected to @{event.unique_id}!")


@client.on(DisconnectEvent)
async def on_disconnect(event: DisconnectEvent):
    client.logger.info("Disconnected.")


@client.on(LiveEndEvent)
async def on_liveend(event: LiveEndEvent):
    client.logger.info(f"Live Ended {ControlAction(event.action).name}")
    trigger_streamer_bot(offline_action, streamer_bot_url, api_key)
    client.disconnect()


@client.on(ControlEvent)
async def on_control(event: ControlEvent):
    client.logger.info(f"Control event {ControlAction(event.action).name}")

async def check_loop():
    """
    Haupt-Loop zur Überprüfung des Live-Status und Triggern von Aktionen.
    """
    is_live = False
    was_live = False
    try:
        while True:
            try:
                is_live = await client.is_live()
                if is_live:
                    if not was_live:
                        client.logger.info(f"Kanal ist live! Sende '{live_action}' an Streamer.bot.")
                        trigger_streamer_bot(live_action, streamer_bot_url, api_key)
                        was_live = True

                    # Verbinde nur, wenn der Kanal live ist
                    if not client.connected:
                        await client.connect()
                else:
                    if was_live:
                        client.logger.info(f"Kanal ist offline. Sende '{offline_action}' an Streamer.bot.")
                        trigger_streamer_bot(offline_action, streamer_bot_url, api_key)
                        was_live = False
                    else:
                        client.logger.info(
                            f"Kanal ist offline. Keine Aktion an Streamer.bot gesendet \nWarte auf Start des LiveStreams...")

                # Wartezeit zwischen Überprüfungen
                client.logger.info(f"Warte 60 Sekunden auf erneute Überprüfung")
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