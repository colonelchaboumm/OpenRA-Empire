"""Small symmetric settlements for human multiplayer and courier validation."""
def write_maps(mod, terrain, header, rules):
    player_base = header.split('Players:')[0]
    neutral = header.split('Players:')[1].split('\tPlayerReference@Village:')[0]
    for count, folder, title in ((2, 'deux-bourgs', 'Deux Bourgs - multijoueur'), (3, 'trois-bourgs', 'Trois Bourgs - multijoueur')):
        text = player_base.replace('Le Bourg - economie medievale', title) + 'Players:' + neutral
        centers = [(16, 19), (44, 19)] + ([(30, 46)] if count == 3 else [])
        for i, color in enumerate(('6C91B3', 'B9634C', '9A79B4')):
            if i >= count: break
            text += f'\tPlayerReference@Bourg{i}:\n\t\tName: Bourg{i}\n\t\tPlayable: True\n\t\tAllowBots: False\n\t\tRequired: True\n\t\tLockSpawn: True\n\t\tSpawn: {i+1}\n\t\tLockFaction: True\n\t\tFaction: england\n\t\tColor: {color}\n'
        text += 'Actors:\n'; index = 0
        def actor(name, x, y, owner, stock=None):
            nonlocal text, index
            text += f'\tActor{index}: {name}\n\t\tOwner: {owner}\n\t\tLocation: {x+32},{y-32}\n'
            if stock: text += f'\t\tMedievalStock: {stock}\n'
            index += 1
        for i, (x, y) in enumerate(centers):
            owner = f'Bourg{i}'
            actor('mpspawn', x, y, 'Neutral')
            actor('md.warehouse', x, y, owner, '180, 100, 80, 100')
            actor('md.market', x, y+5, owner, '0, 40, 0, 0')
            for name,dx,dy in [('house',4,0),('barracks',4,5),('sawmill',-7,0),('quarry',8,0),
                ('farm',-7,5),('field',-7,9),('field',-4,9),('ranch',4,-6),('pasture',8,-6),
                ('builder',0,4),('builder',1,4),('hauler',-1,4),('hauler',-2,4),('lumberjack',-6,3),
                ('stonecutter',9,3),('farmer',-6,8),('breeder',5,-2)]:
                actor('md.'+name,x+dx,y+dy,owner)
            for j,(dx,dy) in enumerate([(-9,-5),(-7,-5),(-5,-5),(-9,-3),(-7,-3),(-5,-3)]):
                actor('md.'+('tree','pine','birch')[j%3],x+dx,y+dy,'Neutral')
            for dx,dy in [(8,-3),(10,-3),(11,-1),(11,1)]:actor('md.stone',x+dx,y+dy,'Neutral')
        path=mod/'maps'/folder;path.mkdir(exist_ok=True)
        # Explored diplomatic sandbox, with standard player relations and no forced teams.
        (path/'map.yaml').write_text(text+rules,encoding='utf-8',newline='\n')
        (path/'map.bin').write_bytes(terrain)
    test=header.split('Actors:')[0].replace('Visibility: Lobby','Visibility: MissionSelector').replace('Le Bourg - economie medievale','Validation des messagers')
    for name in ('Rival','Third'):
        test+=f'\tPlayerReference@{name}:\n\t\tName: {name}\n\t\tFaction: england\n\t\tEnemies: Village\n'
    test+='Actors:\n\tSpawn: mpspawn\n\t\tOwner: Neutral\n\t\tLocation: 61,-3\n'
    path=mod/'maps/messenger-test';path.mkdir(exist_ok=True)
    (path/'map.yaml').write_text(test+rules,encoding='utf-8',newline='\n')
    (path/'map.bin').write_bytes(terrain)
