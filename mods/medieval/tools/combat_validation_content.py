"""Rebuild a native combat test mod without changing the player's normal rules."""
from pathlib import Path
MOD=Path(__file__).resolve().parents[1]
TESTS='''World:
	MedievalCombatValidation:
	MedievalWildHerds:
		SpawnAnimals: False
	MedievalDayNight:
		DayTicks: 30000
		NightTicks: 3000
	MedievalWeather:
		FirstRainTicks: 30000

^MDWORKER:
	MedievalCombatProbe:
	MedievalNeedsFood:
		MealInterval: 30000
		StarvationDelay: 60000

^MDOBJECT:
	MedievalCombatProbe:
'''
def main():
    target=MOD.parent/'medieval-combat-content';target.mkdir(exist_ok=True)
    (target/'combat-tests.yaml').write_text(TESTS,encoding='utf-8',newline='\n')
    text=(MOD/'mod.yaml').read_text(encoding='utf-8')
    text=text.replace('\t\t$medieval: medieval','\t\t$medieval: medieval\n\t\t^EngineDir|mods/medieval-combat-content: combat-preview')
    text=text.replace('\tmedieval|firefighting.yaml','\tmedieval|firefighting.yaml\n\tcombat-preview|combat-tests.yaml')
    preview=MOD.parent/'medieval-combat';preview.mkdir(exist_ok=True)
    (preview/'mod.yaml').write_text(text.replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4'),encoding='utf-8',newline='\n')
    (MOD.parents[1]/'analysis/medieval/combat').mkdir(parents=True,exist_ok=True)
if __name__=='__main__': main()
